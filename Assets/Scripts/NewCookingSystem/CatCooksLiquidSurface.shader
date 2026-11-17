// ============================================================================
//  CatCooks / Liquid Surface  (v3)
//
//  Поверхность жидкости: молоко в таре, сливки, лужи, струя. URP, Unity 6.
//
//  • Полупрозрачная, мягкий край по альфе вершин (лужа).
//  • Рябь — процедурная; круги от струи (_Rings).
//  • НОВОЕ:
//    – СЛИВКИ (_CreamCover 0..1): густой слой поверх молока. Островки растут и
//      сливаются в шапку, отбрасывают тень на молоко, матовые, со складками.
//      Координаты — uv2 (оси тары), поэтому сливки ездят вместе с ведром.
//    – Воронка при взбивании (_Swirl, _SwirlAmount): сливки закручиваются
//      вокруг щупа, там же пена.
//    – Пена (_Foam): пузырьки ячейками.
//    – Мениск (_Meniscus, uv.x = доля до стенки): светлая кромка у стенки и
//      тонкая линия касания — жидкость «прилипает» к ведру.
//    – Прожилки струи (_FlowStreaks, uv.y = длина по струе): струя «течёт».
//    – v3.1: плотность (_Density) — молоко не просвечивает, меньше «плоского»
//      пересвета, тёплая глубина у стенок; пенка (_Skin) — мелкие пузырьки
//      кольцом у стенок и пятнами; пена при взбивании — крупные пузыри с тенью.
//    – v4: жирные «глазки» на молоке (_Fat): круглые блестящие капли жира с тонкой
//      мультяшной обводкой и бликом, сбиваются к стенкам; след щупа (_Trail,
//      _TrailAmount): светлая полоса пены там, где водили вибратором, тает.
//  Всё новое по умолчанию выключено — лужи и старые материалы выглядят как раньше.
// ============================================================================
Shader "CatCooks/Liquid Surface"
{
    Properties
    {
        _BaseColor ("Цвет жидкости", Color) = (0.97, 0.97, 0.95, 1)
        _Opacity ("Непрозрачность (молоко ~0.95, вода ~0.5)", Range(0, 1)) = 0.95
        _EdgeSoftness ("Мягкость края", Range(0, 1)) = 0.6

        _RippleStrength ("Сила ряби", Range(0, 1)) = 0.35
        _RippleScale ("Размер ряби (волн на метр)", Float) = 6
        _RippleSpeed ("Скорость ряби", Range(0, 4)) = 1.2
        _Agitation ("Возбуждение (ставит скрипт)", Range(0, 1)) = 0

        _RingSpeed ("Скорость кругов от струи, м/с", Float) = 0.6
        _RingWidth ("Ширина круга, м", Float) = 0.05
        _RingStrength ("Сила кругов", Range(0, 2)) = 0.8

        _Flat ("Плоский свет", Range(0, 1)) = 0.6
        _FlatColor ("Цвет плоского света", Color) = (1.03, 1.0, 0.95, 1)
        _ShadowColor ("Цвет тени", Color) = (0.85, 0.8, 0.78, 1)
        _SkyTint ("Небесный свет сверху", Color) = (0.96, 0.98, 1.04, 1)
        _GlintColor ("Блик", Color) = (1, 1, 1, 1)
        _GlintSize ("Размер блика", Range(0.8, 0.999)) = 0.965
        _GlintStrength ("Сила блика", Range(0, 3)) = 1.3
        _SheenStrength ("Мягкий отсвет (широкий блик)", Range(0, 1)) = 0.25
        _FresnelColor ("Френель", Color) = (1, 1, 1, 1)
        _FresnelPower ("Ширина френеля", Range(1, 8)) = 4
        _FresnelStrength ("Сила френеля", Range(0, 1)) = 0.35

        [Header(Cream layer)]
        _CreamColor ("Сливки: цвет", Color) = (1.0, 0.95, 0.80, 1)
        _CreamCover ("Сливки: покрытие (ставит скрипт)", Range(0, 1)) = 0
        _CreamScale ("Сливки: островков на метр", Float) = 11
        _CreamFold ("Сливки: складки", Range(0, 1)) = 0.7
        _CreamShadow ("Сливки: тень на молоко", Range(0, 1)) = 0.45
        _CreamRim ("Сливки: светлая кромка островков", Range(0, 1)) = 0.35

        [Header(Whisk and foam)]
        _Swirl ("Воронка: xy центр, z радиус, w фаза (скрипт)", Vector) = (0, 0, 0.08, 0)
        _SwirlAmount ("Воронка: сила (скрипт)", Range(0, 1)) = 0
        _Foam ("Пена (скрипт)", Range(0, 1)) = 0
        _FoamScale ("Пена: пузырьков на метр", Float) = 70
        _Skin ("Пенка на молоке (скрипт)", Range(0, 1)) = 0
        _Density ("Плотность: 1 — не просвечивает, объёмный свет (скрипт)", Range(0, 1)) = 0
        _Paste ("Паста: 0 — жидкость, 1 — густое масло (скрипт)", Range(0, 1)) = 0

        [Header(Meniscus and stream)]
        _Meniscus ("Мениск у стенки (скрипт)", Range(0, 1)) = 0
        _FlowStreaks ("Прожилки струи", Range(0, 1)) = 0
        _StreakScale ("Прожилки: частота", Float) = 9

        [Header(Milk fat and trails v4)]
        _Fat ("Жирные глазки (скрипт)", Range(0, 1)) = 0
        _FatScale ("Глазки: размер (больше — мельче)", Float) = 42
        _FatColor ("Глазки: цвет жира", Color) = (1.0, 0.95, 0.78, 1)
        _FatLine ("Глазки: обводка", Range(0, 1)) = 0.55
        _TrailAmount ("След щупа (скрипт)", Range(0, 1)) = 0
        _TrailWidth ("След: ширина, м", Float) = 0.014

        [Header(Butter v10)]
        _ButterCream ("Масло: светлый сливочный верх", Color) = (1.0, 0.97, 0.8, 1)
        _ButterDeep ("Масло: тёплая тень (золотистая)", Color) = (0.93, 0.68, 0.28, 1)
        _ButterCurd ("Масло: рыхлость (комочки)", Range(0, 1)) = 0.7
        _ButterMarks ("Масло: следы взбивания", Range(0, 1)) = 0.75
        _ButterSoft ("Масло: мягкость переходов света (мультяшно — меньше)", Range(0, 1)) = 0.25
        _ButterBeans ("Масло: белые блики-бобы", Range(0, 1)) = 0.8

        [Header(Anime v23_4)]
        _Anime ("Аниме-свет (0 — как раньше, 1 — ступенчатый мультяшный)", Range(0, 1)) = 1
        _AnimeSoft ("Аниме: мягкость ступени тени", Range(0.005, 0.15)) = 0.04
        _AnimeBands ("Аниме: ступеней света (2 или 3)", Range(2, 3)) = 3
        _AnimePaint ("Аниме: живописные пятна тона", Range(0, 1)) = 0.5
        _AnimePaintScale ("Аниме: пятен на метр", Float) = 3.5
        _AnimeRim ("Аниме: контурный свет по краю", Range(0, 1)) = 0.5
        _AnimeWallShadow ("Аниме: тень от борта серпом (скрипт)", Range(0, 1)) = 0
        _AnimeCenter ("Аниме: центр поверхности, мир (скрипт)", Vector) = (0, 0, 0, 0)
        _AnimeCaustic ("Аниме: каустика-сетка, вода (скрипт)", Range(0, 1)) = 0
        _AnimeCausticScale ("Аниме: ячеек каустики на метр", Float) = 6
        _AnimeShine ("Аниме: блик-«окно» дугами у края (скрипт)", Range(0, 1)) = 0.4
        _AnimeCelize ("Аниме v23.5: плоские ступени тона (3 краски)", Range(0, 1)) = 1
        _AnimeDepth ("Аниме v23.5: тёмная полоса глубины у стенки", Range(0, 0.5)) = 0.12
        _AnimeInk ("Аниме v23.5: тушевой контур у стенки", Range(0, 1)) = 0.45
        _AnimeFlecks ("Аниме v23.5: блёстки-штрихи (скрипт)", Range(0, 1)) = 0
        _AnimeFleckScale ("Аниме v23.5: штрихов на метр", Float) = 14
        _AnimeFleckColor ("Аниме v23.5: цвет штрихов (скрипт)", Color) = (1, 1, 1, 1)
        _AnimeVeins ("Аниме v23.5: светящиеся прожилки (скрипт)", Range(0, 1)) = 0
        _AnimeVeinColor ("Аниме v23.5: цвет прожилок (скрипт)", Color) = (1, 0.82, 0.3, 1)
        _AnimePores ("Аниме v23.5: поры рыхлого теста (скрипт)", Range(0, 1)) = 0
        _AnimePoreScale ("Аниме v23.5: пор на метр", Float) = 16
        _AnimeGlow ("Аниме: свечение изнутри, соки (скрипт)", Range(0, 1)) = 0
        _AnimeBubbles ("Аниме: пузырьки внутри, мёд/сироп (скрипт)", Range(0, 1)) = 0
        _AnimeLayers ("Аниме v23.10: многослойность — озерцо, ступени к стенке, складки, мениск, блик-окно (скрипт)", Range(0, 1)) = 0
        _AnimeBreath ("Аниме v23.15: поверхность «дышит» — сдвиг по нормали, м (скрипт)", Float) = 0
        _AnimeRadius ("Аниме v23.24: радиус тары в осях меша (скрипт; 0 — не масштабировать узор)", Float) = 0
        _AnimeKind ("Аниме v23.10: вид 0 гладкое, 1 сметана, 2 меренга, 3 тесто, 4 шоколад, 5 мороженое, 6 вода, 7 мёд, 8 яичная масса, 9 карамель (скрипт)", Range(0, 9)) = 0

        // Режим рисования (плотное молоко ставит скрипт: One/Zero/ZWrite 1, очередь 2450).
        [HideInInspector] _SrcBlend ("", Float) = 5
        [HideInInspector] _DstBlend ("", Float) = 10
        [HideInInspector] _ZWrite ("", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half  _Opacity, _EdgeSoftness;
                half  _RippleStrength; float _RippleScale; half _RippleSpeed, _Agitation;
                float _RingSpeed, _RingWidth; half _RingStrength;
                half  _Flat; half4 _FlatColor, _ShadowColor, _SkyTint;
                half4 _GlintColor; half _GlintSize, _GlintStrength, _SheenStrength;
                half4 _FresnelColor; half _FresnelPower, _FresnelStrength;
                half4 _CreamColor; half _CreamCover; float _CreamScale; half _CreamFold, _CreamShadow, _CreamRim;
                float4 _Swirl; half _SwirlAmount; half _Foam; float _FoamScale; half _Skin, _Density, _Paste;
                half _Meniscus, _FlowStreaks; float _StreakScale;
                half _Fat; float _FatScale;
                half _Anime, _AnimeSoft, _AnimeBands, _AnimePaint, _AnimeRim, _AnimeWallShadow, _AnimeCaustic, _AnimeGlow, _AnimeBubbles, _AnimeShine;
                float _AnimePaintScale, _AnimeCausticScale; float4 _AnimeCenter;
                half _AnimeCelize, _AnimeDepth, _AnimeInk, _AnimeFlecks, _AnimeVeins, _AnimePores;
                half _AnimeLayers, _AnimeKind; float _AnimeBreath; float _AnimeRadius;
                float _AnimeFleckScale, _AnimePoreScale; half4 _AnimeFleckColor, _AnimeVeinColor;
                half4 _ButterCream, _ButterDeep; half _ButterCurd, _ButterMarks, _ButterSoft, _ButterBeans; half4 _FatColor; half _FatLine; half _TrailAmount; float _TrailWidth;
            CBUFFER_END
            // След щупа: xy — точка (оси тары), z — возраст, с, w — сила (0 — нет). [0] — свежая.
            float4 _Trail[8];
            // Круги от струи: xyz — центр (мир), w — возраст, с (<0 — нет)
            float4 _Rings[4];
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CatCooksAnime.hlsl"

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv0        : TEXCOORD0;   // x — доля до стенки (или обхват струи), y — длина по струе
                float2 uv1        : TEXCOORD1;   // координаты в осях тары, м (сливки, воронка)
                float2 uv2        : TEXCOORD2;   // x — маска масла (растёт от стенок), y — маска сметаны; нет канала — 0
                float2 uv3        : TEXCOORD3;   // x — мороженое, y — лёд от азота; нет канала — 0
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                float4 uv         : TEXCOORD3;
                half   fog        : TEXCOORD4;
                half   paste      : TEXCOORD5;
                half   thick      : TEXCOORD6;   // сметана: гладкая, глянцевая, держит форму
                half2  ice        : TEXCOORD7;   // x — мороженое (матовое, ямки), y — иней (искры)
            };

            V vert(A v)
            {
                V o;
                float3 posOS = v.positionOS.xyz;
                if (_AnimeBreath > 1e-6)
                {
                    // v23.15: «дыхание» вязкой массы (пособие, 3.2: шум × нормаль → позиция) — медленные мягкие волны
                    float tb = _Time.y;
                    float2 ub = v.uv1 / max(1.0, _AnimeRadius / 0.18);       // v23.26: в большой таре волны крупнее (не иглы)
                    float bn = sin(ub.x * 23.0 + tb * 1.1) * sin(ub.y * 19.0 - tb * 0.9) * 0.5 + sin(ub.x * 9.0 - ub.y * 7.0 + tb * 0.6) * 0.5;
                    posOS += v.normalOS * bn * _AnimeBreath * (1.0 - pow(saturate(v.uv0.x), 4.0));
                }
                VertexPositionInputs p = GetVertexPositionInputs(posOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = float4(v.uv0, v.uv1);
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.paste = v.uv2.x;
                o.thick = v.uv2.y;
                o.ice = v.uv3;
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 hash22(float2 p) { float n = hash21(p); return float2(n, hash21(p + n + 17.17)); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0, a = 0.55;
                [unroll] for (int k = 0; k < 4; k++) { s += vnoise(p) * a; p = p * 2.03 + float2(3.1, 1.7); a *= 0.5; }
                return s / 0.97;
            }
            // Расстояние до центра ближайшей ячейки (пузырьки пены).
            float cells(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float d = 8;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = hash22(i + g) * 0.8 + 0.1;
                    d = min(d, length(g + o - f));
                }
                return d;
            }

            // Высота ряби в точке (для нормали — через разности).
            float RippleH(float2 xz, float t, float amp)
            {
                float s = _RippleScale;
                float h = sin(dot(xz, float2(0.8, 0.6)) * s + t * 1.7) * 0.5
                        + sin(dot(xz, float2(-0.5, 0.9)) * s * 1.37 - t * 2.1) * 0.35
                        + (vnoise(xz * s * 0.7 + t * 0.35) - 0.5) * 0.9;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float age = _Rings[k].w;
                    if (age < 0) continue;
                    float r = length(xz - _Rings[k].xz);
                    float front = age * _RingSpeed;
                    float d = (r - front) / max(1e-3, _RingWidth);
                    h += exp(-d * d) * sin(d * 3.0) * _RingStrength * saturate(1.0 - age * 0.8);
                }
                return h * amp;
            }

            // Закрутка координат вокруг щупа.
            float2 SwirlUV(float2 p)
            {
                float2 d = p - _Swirl.xy;
                float fall = exp(-dot(d, d) / max(1e-4, _Swirl.z * _Swirl.z));
                float ang = _Swirl.w * fall * _SwirlAmount * 2.0 + fall * _SwirlAmount * sin(_Time.y * 9.0) * 0.4;
                float s, c; sincos(ang, s, c);
                return _Swirl.xy + float2(c * d.x - s * d.y, s * d.x + c * d.y);
            }

            // Маска сливок 0..1 и «высота» слоя (для нормали).
            float CreamMask(float2 p, float cover, float swirlFall)
            {
                if (cover <= 0.001) return 0;
                float n = fbm(p * _CreamScale);
                n += swirlFall * _SwirlAmount * 0.18;             // там, где взбивают, сливки собираются первыми
                float thr = lerp(0.86, 0.02, cover);
                return smoothstep(thr - 0.05, thr + 0.05, n);
            }

            // v10.2: МУЛЬТЯШНОЕ масло — крупные простые комочки + несколько чистых спиральных бороздок.
            // groove — линия бороздки 0..1 (для цвета), curd — «шапка» комочка 0..1.
            float ButterH(float2 q, out float curd, out float groove)
            {
                float rr = length(q);
                float ang = atan2(q.y, q.x);
                float wob = vnoise(q * 7.0 + 3.7);
                // следы щупа: 2 спирали, тонкие чистые линии; к центру гаснут (без «звёздочки»)
                float m1 = sin(rr * 95.0 + ang * 2.0 + wob * 2.5);
                float m2 = sin(rr * 61.0 - ang * 1.0 + wob * 2.0 + 1.7);
                groove = max(smoothstep(0.86, 0.97, m1), smoothstep(0.9, 0.98, m2) * 0.8);
                groove *= smoothstep(0.03, 0.09, rr) * smoothstep(0.3, 0.55, vnoise(q * 4.0 + 9.1) + 0.25);
                // крупные мягкие комочки (рыхлость), мелочи нет — мультяшно
                float c1 = cells(q * 13.0 + 5.1);
                curd = 1.0 - smoothstep(0.15, 0.6, c1);
                return curd * 0.8 * _ButterCurd - groove * 0.6 * _ButterMarks;
            }

            half4 frag(V i) : SV_Target
            {
                half pk = saturate(max(_Paste, i.paste));       // масло: общее (скрипт) или по вершинам
                float t = _Time.y * _RippleSpeed;
                half tk = saturate(i.thick);
                half ik = saturate(i.ice.x), fk = saturate(i.ice.y);
                float amp = (_RippleStrength * (0.4 + _Agitation * 1.6)) * 0.02 * (1.0 - max(max(pk, tk), max(ik, fk)));   // густое и мёрзлое не рябит
                float e = 0.01;
                float2 xz = i.positionWS.xz;
                float h0 = RippleH(xz, t, amp);
                float hx = RippleH(xz + float2(e, 0), t, amp);
                float hz = RippleH(xz + float2(0, e), t, amp);
                float3 rip = float3(-(hx - h0) / e, 0, -(hz - h0) / e);
                float3 nWS = normalize(normalize(i.normalWS) + rip);

                // v10: масло — рыхлое, комочками, со следами взбивания (мягкий рельеф вместо «наждака»)
                float butterCurd = 0, butterH = 0, butterGroove = 0;
                if (pk > 0.001)
                {
                    float be = 0.005;
                    float cdx, cdz, gdx, gdz;
                    butterH = ButterH(i.uv.zw, butterCurd, butterGroove);
                    float hx2 = ButterH(i.uv.zw + float2(be, 0), cdx, gdx);
                    float hz2 = ButterH(i.uv.zw + float2(0, be), cdz, gdz);
                    float3 bn = float3(-(hx2 - butterH) / be, 0, -(hz2 - butterH) / be) * 0.01;
                    nWS = normalize(nWS + bn * pk);
                }

                // Мороженое: шершавое, с мелкими ямками-дырочками (как после ложки-скупа).
                half icePit = 0;
                if (ik > 0.001)
                {
                    float2 ip = i.uv.zw * 55.0;
                    float c0 = cells(ip), cx = cells(ip + float2(0.08, 0)), cz = cells(ip + float2(0, 0.08));
                    icePit = 0;                                                   // v23.34: дырочки-«кубики» убраны (рисует аниме-слой)
                    float2 gp = i.uv.zw * 210.0;
                    float g0 = vnoise(gp), gx = vnoise(gp + float2(0.4, 0)), gz = vnoise(gp + float2(0, 0.4));
                    float3 bump = float3(-(cx - c0) * 2.2 - (gx - g0) * 0.7, 0, -(cz - c0) * 2.2 - (gz - g0) * 0.7);
                    nWS = normalize(nWS + bump * ik * 0.25);                      // v23.34: шершавость еле-еле
                }
                if (fk > 0.001)
                {
                    float2 fp = i.uv.zw * 120.0;
                    float f0 = vnoise(fp), fx = vnoise(fp + float2(0.3, 0)), fz = vnoise(fp + float2(0, 0.3));
                    nWS = normalize(nWS + float3(-(fx - f0), 0, -(fz - f0)) * fk * 0.5);
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));

                // ── сливки ──
                float2 cuv = SwirlUV(i.uv.zw);
                float2 sd = i.uv.zw - _Swirl.xy;
                float swirlFall = exp(-dot(sd, sd) / max(1e-4, _Swirl.z * _Swirl.z));
                float cm = 0, creamShadow = 0;
                if (_CreamCover > 0.001)
                {
                    cm = CreamMask(cuv, _CreamCover, swirlFall) * (1.0 - pk) * (1.0 - tk) * (1.0 - ik);
                    float ce = 0.006;
                    float cmx = CreamMask(cuv + float2(ce, 0), _CreamCover, swirlFall);
                    float cmz = CreamMask(cuv + float2(0, ce), _CreamCover, swirlFall);
                    // Складки густого слоя.
                    float fold = fbm(cuv * _CreamScale * 2.7 + 11.3);
                    float fx = fbm((cuv + float2(ce, 0)) * _CreamScale * 2.7 + 11.3);
                    float fz = fbm((cuv + float2(0, ce)) * _CreamScale * 2.7 + 11.3);
                    float3 cn = float3(-((cmx - cm) * 0.9 + (fx - fold) * _CreamFold * 0.5) / ce * 0.01, 0,
                                       -((cmz - cm) * 0.9 + (fz - fold) * _CreamFold * 0.5) / ce * 0.01);
                    // Сливки гасят рябь (густые) и имеют свой рельеф.
                    nWS = normalize(lerp(nWS, normalize(i.normalWS), cm * 0.8) + cn * (0.4 + cm * 0.6));
                    // Тень островка на молоко: маска, сдвинутая к свету.
                    float2 toL = normalize(light.direction.xz + 1e-4) * 0.012;
                    float cl = CreamMask(cuv + toL, _CreamCover, swirlFall);
                    creamShadow = saturate(cl - cm) * _CreamShadow;
                }

                // ── свет ──
                // Паста: свет «жёстче» — бугры и ямки видны.
                half wrap = lerp(lerp(lerp(0.5h, 0.28h, pk), 0.38h, tk), 0.42h, ik);     // сметана, мороженое: форма видна, но мягко
                half ndl = saturate(dot(nWS, light.direction) * (1.0h - wrap) + wrap);
                half sh = lerp(0.65h, 1.0h, light.shadowAttenuation);
                half3 diffuse = lerp(_ShadowColor.rgb, light.color, ndl) * sh;
                diffuse = lerp(diffuse, _FlatColor.rgb, _Flat * (1.0h - 0.5h * _Density) * (1.0h - 0.6h * pk));
                diffuse *= lerp(1.0h, _SkyTint.rgb, saturate(nWS.y));
                // ── v23.4: АНИМЕ-СВЕТ — ступеньки и цветная тень вместо серой, тень от борта серпом ──
                half3 animeBase = _BaseColor.rgb * i.color.rgb;
                half3 animeShadow = CCA_ShadowMul(animeBase, _Density);
                if (_Anime > 0.001h)
                {
                    half3 cel = CCA_Cel(animeBase, nWS, light, _AnimeSoft, _AnimeBands, _Density);
                    if (_AnimeWallShadow > 0.001h)
                    {
                        float2 dc = i.positionWS.xz - _AnimeCenter.xz;
                        float2 lxz = light.direction.xz;
                        half side = (dot(dc, dc) > 1e-8 && dot(lxz, lxz) > 1e-6) ? saturate(dot(normalize(dc), normalize(lxz))) : 0.0h;
                        half wdt = _AnimeWallShadow * (0.1h + 0.3h * (1.0h - saturate(light.direction.y))) * side;
                        half crescent = smoothstep(1.0h - wdt - 0.012h, 1.0h - wdt + 0.012h, i.uv.x) * step(0.04h, side);
                        cel = lerp(cel, animeShadow * lerp(half3(1, 1, 1), min(light.color, 1.2h), 0.6h), crescent);
                    }
                    diffuse = lerp(diffuse, cel, _Anime);
                }

                half3 milk = _BaseColor.rgb * i.color.rgb * (1.0h - creamShadow);
                // Плотное молоко: не пересвечено в чистый белый, тёплая «глубина» у стенок.
                half wallDepth = smoothstep(0.6, 1.0, i.uv.x);
                milk *= lerp(1.0h, half3(0.965h, 0.95h, 0.92h), _Density);
                milk *= 1.0h - wallDepth * 0.07h * _Density;
                milk += half3(0.03h, 0.025h, 0.015h) * (1.0h - wallDepth) * _Density;   // свет изнутри толщи
                half3 cream = _CreamColor.rgb;
                // Светлая кромка островков (толщина слоя).
                half rim = saturate(cm * (1.0 - cm) * 4.0) * _CreamRim;
                half3 col = lerp(milk, cream * (1.0h + rim * 0.25h), cm) * diffuse;

                // ── v10: МАСЛО — сливочное, желтоватое, мягкие переходы (как на картинках):
                // светлый бледно-кремовый верх → золотистая тёплая тень, в ямках и следах — теплее, на комочках — светлее.
                if (pk > 0.001)
                {
                    half3 prod = i.color.rgb;                                           // цвет продукта (масло)
                    half3 creamTop = lerp(prod, _ButterCream.rgb, 0.55h);
                    half3 deep = prod * _ButterDeep.rgb * 1.05h;
                    half3 midC = lerp(prod, creamTop, 0.35h);
                    // v10.2: ТРИ ТОНА, как в мультике: золотистая тень → основной → бледный сливочный свет
                    half nl = dot(nWS, light.direction) * 0.5h + 0.5h + (butterCurd - 0.5h) * 0.18h;
                    half soft = lerp(0.015h, 0.12h, _ButterSoft);
                    half b1 = smoothstep(0.42h - soft, 0.42h + soft, nl);
                    half b2 = smoothstep(0.7h - soft, 0.7h + soft, nl);
                    half3 bcol = lerp(lerp(deep, midC, b1), creamTop, b2);
                    // бороздки — нарисованная тёплая линия со светлой кромкой с одной стороны
                    half3 grooveC = deep * 0.92h;
                    bcol = lerp(bcol, grooveC, butterGroove * 0.85h * _ButterMarks);
                    bcol *= lerp(0.82h, 1.0h, step(0.5h, sh));                          // тень от предметов — ступенькой
                    bcol *= lerp(half3(1, 1, 1), light.color, 0.3h);
                    // белые блики-бобы на верхушках комочков (мультяшные)
                    float3 Vb = normalize(GetWorldSpaceViewDir(i.positionWS));
                    half nhB = saturate(dot(nWS, normalize(light.direction + Vb)));
                    half bean = step(0.93h, nhB + butterCurd * 0.06h) * step(0.55h, butterCurd);
                    bcol = lerp(bcol, half3(1.0h, 0.99h, 0.93h), bean * _ButterBeans);
                    // у стенки — тёплая тёмная кромка (мультяшный «контур» по форме ведра)
                    half wallLine = smoothstep(0.9, 0.97, i.uv.x);
                    bcol = lerp(bcol, deep * 0.85h, wallLine * 0.7h);
                    col = lerp(col, bcol, pk);
                }

                // ── пенка: мелкие пузырьки кольцом у стенки и пятнами (на молоке всегда) ──
                if (_Skin > 0.001)
                {
                    float2 sp = i.uv.zw;
                    half patch = saturate(fbm(sp * 9.0 + _Time.y * 0.03) * 1.6 - 0.55);
                    half ringK = smoothstep(0.62, 0.97, i.uv.x);
                    half sk = saturate((ringK * 0.9h + patch * 0.8h) * _Skin * 1.4h) * (1.0h - cm * 0.8h);
                    if (sk > 0.01)
                    {
                        float c1 = cells(sp * _FoamScale * 2.2 + 3.7);
                        float c2 = cells(sp * _FoamScale * 4.1 + 9.1);
                        half b1 = 1.0h - smoothstep(0.05, 0.14, abs(c1 - 0.3));
                        half b2 = 1.0h - smoothstep(0.04, 0.1, abs(c2 - 0.28));
                        half hole = smoothstep(0.26, 0.1, c1) * 0.06h;              // серединка пузырька чуть темнее
                        half3 froth = lerp(col, half3(1.0h, 0.995h, 0.985h) * diffuse, 0.55h) * (1.0h - hole);
                        col = lerp(col, froth + (b1 * 0.08h + b2 * 0.05h) * diffuse, sk * (0.55h + 0.45h * max(b1, b2)));
                    }
                }

                // ── пена ──
                half foam = (1.0h - pk * 0.85h) * (1.0h - tk) * (1.0h - ik) * (1.0h - fk) * saturate(_Foam * (0.55 + 0.45 * vnoise(i.uv.zw * 18.0 + _Time.y * 0.7)) + swirlFall * _SwirlAmount * 0.55);
                if (foam > 0.01)
                {
                    float2 fp = SwirlUV(i.uv.zw) * _FoamScale + float2(_Time.y * 0.4, -_Time.y * 0.3);
                    float c = cells(fp);
                    half bub = 1.0h - smoothstep(0.08, 0.2, abs(c - 0.32));   // кольца пузырьков
                    // Крупные пузыри поверх мелких — густая взбитая пена.
                    float2 fp2 = SwirlUV(i.uv.zw) * _FoamScale * 0.45 + float2(-_Time.y * 0.2, _Time.y * 0.25);
                    float cb = cells(fp2);
                    half big = 1.0h - smoothstep(0.06, 0.16, abs(cb - 0.36));
                    half bigShade = smoothstep(0.34, 0.05, cb) * 0.12h;            // серединка крупного — в тени
                    half froth = saturate(foam * 1.5h);
                    half3 foamCol = lerp(half3(1, 1, 1), _CreamColor.rgb, cm * 0.6h) * diffuse * 1.04h;
                    col = lerp(col, foamCol * (1.0h - bigShade * froth) + big * 0.06h * froth, froth * (0.5h + 0.5h * max(bub, big)));
                }

                // ── v4: след щупа — светлая полоса пены по пути вибратора ──
                if (_TrailAmount > 0.001)
                {
                    float2 q = i.uv.zw;
                    half tr = 0;
                    [unroll] for (int k = 0; k < 7; k++)
                    {
                        float4 A = _Trail[k], B = _Trail[k + 1];
                        if (A.w <= 0 || B.w <= 0) continue;
                        float2 ab = B.xy - A.xy;
                        float hh = saturate(dot(q - A.xy, ab) / max(1e-6, dot(ab, ab)));
                        float dd = length(q - A.xy - ab * hh);
                        float age = lerp(A.z, B.z, hh);
                        float w = _TrailWidth * (1.0 + age * 0.9);               // расплывается
                        half fade = saturate(1.0 - age / 3.0) * min(A.w, B.w);
                        // рваный край полосы — пена, а не краска
                        half rag = 0.7h + 0.6h * vnoise(q * 140.0 + k * 3.1);
                        tr = max(tr, exp(-dd * dd / max(1e-6, w * w)) * fade * rag);
                    }
                    tr = saturate(tr) * _TrailAmount * (1.0h - pk) * (1.0h - tk) * (1.0h - ik);
                    if (tr > 0.001)
                    {
                        float c2 = cells(i.uv.zw * _FoamScale * 1.6 + _Time.y * 0.3);
                        half rim2 = 1.0h - smoothstep(0.05, 0.13, abs(c2 - 0.3));
                        half3 froth2 = half3(1.0h, 0.995h, 0.98h) * diffuse * 1.05h;
                        col = lerp(col, froth2 + rim2 * 0.05h * diffuse, tr * 0.8h);
                    }
                }

                // ── v4: жирные глазки — круглые капли жира с тонкой обводкой и бликом ──
                half fatHl = 0;
                if (_Fat > 0.001)
                {
                    float2 fq = SwirlUV(i.uv.zw) * _FatScale + float2(_Time.y * 0.015, -_Time.y * 0.011);
                    float2 fi = floor(fq), ff = frac(fq);
                    float dmin = 8; float2 dvec = 0; float rr = 0.3;
                    [unroll] for (int fy = -1; fy <= 1; fy++)
                    [unroll] for (int fx = -1; fx <= 1; fx++)
                    {
                        float2 g = float2(fx, fy);
                        float2 h2 = hash22(fi + g);
                        float2 o = h2 * 0.7 + 0.15;
                        float2 dv2 = g + o - ff;
                        float dl = length(dv2) / (0.16 + h2.x * 0.22);          // свой размер у каждого
                        if (dl < dmin) { dmin = dl; dvec = dv2; rr = h2.y; }
                    }
                    // глазки гуще у стенок и пятнами; где сливки — сливаются в шапку
                    half where = saturate(smoothstep(0.55, 0.95, i.uv.x) * 0.8h + saturate(fbm(i.uv.zw * 7.0) * 1.7 - 0.6));
                    half exists = step(rr, saturate(where * 1.2h)) * (1.0h - cm * 0.85h) * (1.0h - pk) * (1.0h - tk) * (1.0h - ik) * (1.0h - fk);
                    half body = (1.0h - smoothstep(0.92, 1.0, dmin)) * exists;
                    if (body > 0.001)
                    {
                        half lineK = smoothstep(0.78, 0.9, dmin) * (1.0h - smoothstep(0.96, 1.02, dmin));
                        half3 fatC = lerp(col, _FatColor.rgb * diffuse * 1.04h, 0.55h);
                        fatC = lerp(fatC, fatC * 0.72h, lineK * _FatLine);           // мультяшная обводка
                        col = lerp(col, fatC, body * _Fat);
                        // блик-точка в верхнем левом углу глазка
                        float2 hp = dvec + float2(0.06, -0.06) * (0.16 + rr * 0.22);
                        fatHl = step(length(hp) / (0.16 + rr * 0.22), 0.28) * body * _Fat;
                    }
                }

                // ── мороженое: тень в ямках, редкие крапинки ванили ──
                if (ik > 0.001)
                {
                    col *= 1.0h - icePit * 0.24h;
                    float2 sc = floor(i.uv.zw * 240.0);
                    half speck = 0.0h;                                            // v23.34: квадратные крапинки убраны (ваниль — круглыми точками)
                    col = lerp(col, col * half3(0.45h, 0.36h, 0.3h), speck * 0.6h);
                }
                // ── иней: холодный отлив и искры ──
                if (fk > 0.001)
                {
                    col = lerp(col, col * half3(0.92h, 0.97h, 1.06h) + 0.04h, fk * 0.6h);
                }

                // ── мениск ──
                half men = smoothstep(0.84, 1.0, i.uv.x) * _Meniscus;
                col *= 1.0h + men * 0.05h;                                        // v23.24: ореол у края тише
                col *= 1.0h - smoothstep(0.985, 1.0, i.uv.x) * 0.1h * _Meniscus;

                // ── прожилки струи ──
                if (_FlowStreaks > 0.001)
                {
                    float st = vnoise(float2(i.uv.x * 7.0, i.uv.y * _StreakScale));
                    col *= 1.0h + (st - 0.5h) * 0.35h * _FlowStreaks;
                }

                // ── v23.4: аниме-слои: живописные пятна, каустика (вода), свечение изнутри (соки), пузырьки (мёд) ──
                if (_Anime > 0.001h)
                {
                    // v23.34: у мороженого (вид 5) аниме-слой РАБОТАЕТ (раньше гасился — шарики и следы ложки не рисовались)
                    half calm = (1.0h - pk) * (1.0h - fk) * ((int)round(_AnimeKind) == 5 ? 1.0h : (1.0h - ik));
                    // v23.5: плоские ступени тона вместо плавного «реалистичного» света
                    col = lerp(col, CCA_Celize(col, animeBase, 0.04h), _AnimeCelize * _Anime * calm);
                    half3 lightC = saturate(animeBase * 1.18h + 0.06h);
                    if (_AnimeWallShadow > 0.001h)
                    {
                        // полоса глубины у стенки (Multiply) + тушевой контур по краю
                        float2 dcB = i.positionWS.xz - _AnimeCenter.xz;
                        float2 lB = light.direction.xz;
                        half sideB = (dot(dcB, dcB) > 1e-8 && dot(lB, lB) > 1e-6) ? saturate(dot(normalize(dcB), normalize(lB))) : 0.0h;
                        half band = smoothstep(0.62h, 0.99h, i.uv.x + 0.05h * sideB) * _AnimeDepth * _Anime * 0.55h;   // v23.24: плавно к стенке, без кольца
                        col *= (1.0h - band) * lerp(half3(1, 1, 1), animeShadow, band * 3.0h);
                        half ink = smoothstep(0.97h, 0.995h, i.uv.x) * _AnimeInk * _Anime;
                        col *= 1.0h - ink * 0.25h;                                       // v23.24: контур у стенки тоньше и тише
                    }
                    if (_AnimePores > 0.001h)
                    {
                        // рыхлость: крупные и мелкие поры
                        half2 po = CCA_Pores(i.uv.zw * _AnimePoreScale);
                        half2 po2 = CCA_Pores(i.uv.zw * _AnimePoreScale * 0.45 + 3.3);
                        half pk2 = _AnimePores * _Anime * (1.0h - ik) * (1.0h - fk);
                        col = lerp(col, col * animeShadow * 0.8h, po.x * pk2 * 0.6h);
                        col = lerp(col, lightC, po.y * pk2 * 0.7h);
                        col = lerp(col, col * animeShadow * 0.85h, po2.x * pk2 * 0.45h);
                        col = lerp(col, lightC, po2.y * pk2 * 0.6h);
                    }
                    if (_AnimeVeins > 0.001h)
                    {
                        half vn = CCA_Veins(i.uv.zw * 6.0, _Time.y * (0.5 + _Agitation));
                        col = lerp(col, _AnimeVeinColor.rgb, vn * _AnimeVeins * _Anime * calm * 0.85h);
                    }
                    if (_AnimeFlecks > 0.001h)
                    {
                        half fkm = CCA_Flecks(i.uv.zw * _AnimeFleckScale, _Time.y * (0.6 + _Agitation * 2.0), _AnimeFlecks);
                        col = lerp(col, _AnimeFleckColor.rgb, fkm * _Anime * calm * 0.85h);
                    }
                    col *= 1.0h + CCA_Paint(i.uv.zw * _AnimePaintScale + 3.1) * 0.12h * _AnimePaint * _Anime;
                    if (_AnimeCaustic > 0.001h)
                    {
                        half cs = CCA_Caustic(i.uv.zw * _AnimeCausticScale, _Time.y * (0.6 + _Agitation));
                        half shallow = 0.6h + 0.4h * smoothstep(0.3h, 0.95h, i.uv.x);    // у края ярче
                        col += cs * shallow * _AnimeCaustic * calm * 0.22h * min(light.color, 1.2h) * lerp(half3(1, 1, 1), animeBase, 0.25h);
                    }
                    if (_AnimeGlow > 0.001h)
                    {
                        half core = 1.0h - smoothstep(0.0h, 0.85h, i.uv.x);
                        col += animeBase * core * core * _AnimeGlow * 0.22h * calm;
                    }
                    if (_AnimeBubbles > 0.001h)
                    {
                        half2 bb = CCA_Bubbles(i.uv.zw * 18.0 + 1.3);
                        half3 bubC = col * 1.14h + 0.03h;
                        col = lerp(col, bubC, bb.x * _AnimeBubbles * calm);
                        col = lerp(col, half3(1, 1, 1), bb.y * _AnimeBubbles * calm * 0.8h);
                    }
                    // ── v23.10: МНОГОСЛОЙНОСТЬ (послойный «сэндвич» из аниме-пособия) ──
                    //   глубина: светлое «озерцо» в центре + ступень тени к стенке; твёрдые тени складок по виду;
                    //   мениск — светлое кольцо у стенки со стороны света; блик-«окно» — пятно и точка
                    if (_AnimeLayers > 0.001h && _AnimeWallShadow > 0.001h)
                    {
                        half Lk = _AnimeLayers * _Anime * calm;
                        float2 dcL = i.positionWS.xz - _AnimeCenter.xz;
                        float angL = atan2(dcL.y, dcL.x);
                        float rL = i.uv.x;
                        float2 lxz = light.direction.xz;
                        float2 ldir = dot(lxz, lxz) > 1e-6 ? normalize(lxz) : float2(0.7, 0.7);
                        float2 pdir = dot(dcL, dcL) > 1e-8 ? normalize(dcL) : float2(0, 0);
                        half toward = dot(pdir, ldir);
                        half3 shC = col * animeShadow;
                        half3 hiC = saturate(col * 1.15h + 0.05h);
                        float nzL = fbm(i.uv.zw * 3.0);
                        half pool = 1.0h - smoothstep(0.4h, 0.43h, rL + (nzL - 0.5) * 0.08 - toward * 0.06);
                        col = lerp(col, hiC, pool * 0.22h * Lk);
                        half outer = smoothstep(0.55h, 0.98h, rL + (nzL - 0.5) * 0.06 + toward * 0.05);
                        col = lerp(col, lerp(col, shC, 0.55h), outer * outer * Lk * 0.18h);   // v23.24: ступень к стенке — мягкий градиент, не кольцо
                        int kd = (int)round(_AnimeKind);
                        half fsh = 0.0h, flt = 0.0h, inkL = 0.0h;
                        // v23.16: узоры в нормированных координатах круга (те же формулы, что рельеф модели в C#)
                        float2 qN = pdir * rL;
                        float wq = 1.5 * sin(angL * 2.0 + rL * 3.0) + 0.8 * sin(qN.x * 4.1 + qN.y * 3.3);
                        if (kd == 1 || kd == 3)
                        {
                            // сметана / тесто: складки модели, лёгкий контур по дальнему склону, светлый гребень
                            float fph = angL * 3.0 + rL * (kd == 1 ? 14.4 : 11.2) + wq;
                            float s1 = sin(fph);
                            float aw = fwidth(s1) + 0.02;
                            half inr = smoothstep(0.08, 0.25, rL) * (1.0h - smoothstep(0.9, 0.97, rL));
                            inkL = (1.0h - smoothstep(0.0, aw * 1.6, abs(s1 - 0.4))) * step(cos(fph), 0.0) * inr * 0.55h;
                            flt = smoothstep(0.9 - aw, 0.9 + aw, s1) * inr * 0.4h;
                            if (kd == 3)
                            {
                                // v23.39: тесто как на фото — гладкое, складки еле видны, мелкие РАВНОМЕРНЫЕ пузырьки по всей поверхности
                                inkL = 0.0h; flt *= 0.55h;                                          // v23.51: без тёмных завихрений — только лёгкие светлые следы миксера
                                float2 pb = qN * 30.0 + float2(3.1, 7.7);
                                float2 ib = floor(pb), fb = frac(pb);
                                float bd = 9.0; float2 bv = float2(0, 0);
                                [unroll] for (int yb = -1; yb <= 1; yb++)
                                [unroll] for (int xb = -1; xb <= 1; xb++)
                                {
                                    float2 ob = float2(xb, yb);
                                    float2 hb = hash22(ib + ob);
                                    float2 vb = ob + 0.2 + hb * 0.6 - fb;
                                    float rb = 0.13 + 0.06 * hb.y;
                                    float db = length(vb) / rb + (hash21(ib + ob + 2.7) > 0.5 ? 9.0 : 0.0);
                                    if (db < bd) { bd = db; bv = vb; }
                                }
                                half farB = saturate(1.6 - fwidth(pb.x) * 2.5);
                                half inB = (1.0h - smoothstep(0.85, 1.0, bd)) * inr * farB;
                                float2 dB = -bv / max(1e-4, length(bv));
                                col = lerp(col, lerp(col, shC, 0.45h), inB * Lk * 0.6h);
                                half rimB = (1.0h - smoothstep(0.0, 0.1 + fwidth(bd), abs(bd - 0.95))) * inr * farB;
                                col = lerp(col, shC * 0.6h, rimB * Lk * 0.45h);
                                half dotB = (1.0h - smoothstep(0.15, 0.3, length(dB * bd - ldir * 0.45))) * inB;
                                col = lerp(col, half3(1, 1, 1), dotB * Lk * 0.7h);
                            }
                        }
                        else if (kd == 2)
                        {
                            // v23.51: МЕРЕНГА — нежная: без спиральных складок и тёмных кромок. Мягкие клубы (рельеф модели, обводка по рельефу ниже),
                            // атласный свет на вершинах клубов, лёгкая сиреневая тень во впадинах, белый кончик пики
                            float c1 = fbm(qN * 2.1 + float2(4.17, 0.93));
                            half bill = smoothstep(0.42h, 0.75h, (half)c1);
                            half inrM = (1.0h - smoothstep(0.85, 0.95, rL));
                            flt = bill * inrM * 0.35h;                                             // светлые вершины клубов
                            fsh = (1.0h - smoothstep(0.25h, 0.45h, (half)c1)) * inrM * 0.25h;     // мягкие впадины
                            float2 pcM = float2(0.1, -0.06);
                            half tip = 1.0h - smoothstep(0.02, 0.05, length(qN - pcM - float2(0.04, 0.02)));
                            col = lerp(col, half3(1, 1, 1), tip * Lk * 0.6h);
                        }
                        else if (kd == 5)
                        {
                            // v23.36: МОРОЖЕНОЕ — без круглых шариков: рыхлый рельеф (обводка по рельефу ниже), следы ложки — пятнами
                            // параллельных изогнутых бороздок, мелкие частые чёрные точки ванили
                            float sA = qN.x * 0.8 + qN.y * 0.6 + 0.35 * sin(qN.y * 3.1 + qN.x * 1.7);
                            float rib = sin(sA * 26.0);
                            float awr = fwidth(rib) + 0.05;
                            half patch = smoothstep(0.48, 0.62, fbm(qN * 2.2 + float2(3.1, 1.7))) * (1.0h - smoothstep(0.85h, 0.95h, rL));
                            patch *= saturate(1.4 - fwidth(sA * 26.0) * 0.8);
                            inkL = max(inkL, (1.0h - smoothstep(0.0, awr * 1.6, abs(rib + 0.75))) * patch * 0.3h);
                            flt = max(flt, smoothstep(0.6 - awr, 0.75 + awr, rib) * patch * 0.25h);
                            float2 vp = qN * 42.0;
                            float2 iv = floor(vp), fv = frac(vp);
                            half dot5 = 0.0h;
                            [unroll] for (int y5 = -1; y5 <= 1; y5++)
                            [unroll] for (int x5 = -1; x5 <= 1; x5++)
                            {
                                float2 o5 = float2(x5, y5);
                                float2 h5 = hash22(iv + o5 + 5.5);
                                half m5 = step(hash21(iv + o5 + 8.8), 0.35);
                                float r5 = 0.05 + 0.04 * h5.y;
                                float d5 = length(o5 + 0.15 + 0.7 * h5 - fv);
                                float aw5 = fwidth(d5) + 0.01;
                                dot5 = max(dot5, (1.0h - smoothstep(r5 - aw5, r5 + aw5, d5)) * m5);
                            }
                            dot5 *= saturate(1.6 - fwidth(vp.x) * 3.0) * (1.0h - smoothstep(0.9h, 0.97h, rL));
                            col = lerp(col, half3(0.07h, 0.05h, 0.04h), dot5 * Lk * 0.9h);
                        }
                        else if (kd == 8)
                        {
                            // v23.21: ЯИЧНАЯ МАССА — разводы медленно перетекают; кучки ОБЪЁМНЫХ прозрачных пузырьков (купол со светом и тенью),
                            // пузырьки чуть плавают, по одному лопаются и появляются снова
                            float tt8 = _Time.y;
                            float w8 = 1.4 * sin(qN.y * 4.3 + tt8 * 0.15) + 0.9 * sin(qN.x * 3.7 - tt8 * 0.11 + 1.2);
                            float mk = sin(qN.x * 6.0 + w8 * 1.3 + sin(qN.y * 7.0 + 1.7 + tt8 * 0.09) * 1.2) * 0.5 + 0.5;
                            mk = mk * 0.6 + fbm(i.uv.zw * 4.0 + float2(2.2, 2.2) + tt8 * 0.03) * 0.4;
                            half yolkS = smoothstep(0.55h, 0.62h, mk), whiteS = 1.0h - smoothstep(0.3h, 0.38h, mk);
                            half3 yolkC = saturate(col * half3(1.06h, 0.82h, 0.5h));
                            half3 whiteC = lerp(col, half3(1.0h, 0.97h, 0.86h), 0.55h);
                            col = lerp(col, yolkC, yolkS * Lk * 0.75h);
                            col = lerp(col, whiteC, whiteS * Lk * 0.6h);
                            // v23.22: пузыри пены — настоящие купола (CCFoamField), тут их больше не рисуем
                        }
                        else if (kd == 7)
                        {
                            // мёд, карамель: ленты-петли (как ложится струя), белые глянцевые полосы по гребням, контур по краю ленты
                            float2 lc0 = float2(0.05, 0.02), lc1 = float2(-0.18, 0.1), lc2 = float2(0.2, -0.15), lc3 = float2(0.0, -0.05);
                            float4 lr = float4(0.38, 0.25, 0.22, 0.55);
                            float d0 = length(qN - lc0) - lr.x, d1 = length(qN - lc1) - lr.y, d2 = length(qN - lc2) - lr.z, d3 = length(qN - lc3) - lr.w;
                            float rib = max(max(exp(-(d0 / 0.06) * (d0 / 0.06)), exp(-(d1 / 0.06) * (d1 / 0.06))), max(exp(-(d2 / 0.06) * (d2 / 0.06)), exp(-(d3 / 0.06) * (d3 / 0.06))));
                            float cr = max(max(exp(-(d0 / 0.018) * (d0 / 0.018)), exp(-(d1 / 0.018) * (d1 / 0.018))), max(exp(-(d2 / 0.018) * (d2 / 0.018)), exp(-(d3 / 0.018) * (d3 / 0.018))));
                            float fwd = fwidth(d0) + 0.006;
                            float ek = max(max(1.0 - smoothstep(0.0, fwd * 1.5, abs(d0 - 0.07)), 1.0 - smoothstep(0.0, fwd * 1.5, abs(d1 - 0.07))),
                                           max(1.0 - smoothstep(0.0, fwd * 1.5, abs(d2 - 0.07)), 1.0 - smoothstep(0.0, fwd * 1.5, abs(d3 - 0.07))));
                            half inr = smoothstep(0.05, 0.2, rL) * (1.0h - smoothstep(0.9, 0.97, rL));
                            flt = rib * 0.45h * inr;
                            inkL = ek * 0.5h * inr;
                            col = lerp(col, half3(1, 1, 1), cr * smoothstep(-0.2h, 0.5h, toward) * Lk * 0.55h * inr);
                        }
                        else if (kd == 9)
                        {
                            // v23.63: КАРАМЕЛЬ — 7 лёгких хвостов-стёков от стенки к центру (та же формула, что CaramelTails в C#):
                            // по гребню хвоста — тёплый глянцевый блик к свету, по краю — тонкая мягкая тень; в центре глубже
                            // и насыщеннее (толще слой), у стенки светлее (тонкий слой, просвечивает)
                            half tlC = 0.0h, crC = 0.0h, edC = 0.0h;
                            [unroll] for (int ck = 0; ck < 7; ck++)
                            {
                                float fk9 = ck * 0.618;
                                float e9 = 0.25 + 0.25 * (fk9 - floor(fk9));
                                float sAl = saturate((1.0 - rL) / (1.0 - e9));
                                float ac9 = ck * 0.8976 + 0.35 * sin(ck * 2.1) + 0.35 * (1.0 - rL) * (1.0 + 0.3 * sin((float)ck));
                                float da9 = angL - ac9;
                                da9 -= 6.2831853 * round(da9 / 6.2831853);
                                float d9 = da9 * rL;
                                float w9 = 0.09 * (1.0 - 0.7 * sAl) + 0.012;
                                float acr9 = exp(-(d9 / w9) * (d9 / w9));
                                float on9 = step(e9 - 0.03, rL) * step(rL, 0.985);
                                float m9 = (1.0 - smoothstep(0.8, 1.0, sAl)) * (1.0 - smoothstep(0.95, 0.985, rL)) * on9;
                                half a9 = (half)(acr9 * m9);
                                tlC = max(tlC, a9);
                                crC = max(crC, (half)(pow(acr9, 6.0) * m9));
                                edC = max(edC, (half)(smoothstep(0.18, 0.32, acr9) * (1.0 - smoothstep(0.32, 0.48, acr9)) * m9));
                            }
                            half3 deepC = saturate(col * half3(0.86h, 0.7h, 0.5h));
                            col = lerp(col, deepC, (half)((1.0 - rL) * 0.35) * Lk);                  // толще — темнее и насыщеннее
                            col = lerp(col, saturate(col * 1.12h + half3(0.04h, 0.02h, 0.0h)), (half)smoothstep(0.75, 0.95, rL) * 0.35h * Lk);   // у стенки тоньше — светлее
                            flt = tlC * 0.35h;
                            inkL = edC * 0.22h;
                            col = lerp(col, half3(1.0h, 0.96h, 0.85h), crC * smoothstep(-0.3h, 0.6h, toward) * Lk * 0.5h);
                        }
                        if (kd == 1 || kd == 2 || kd == 5)
                        {
                            // v23.28: ОБВОДКА ПО РЕЛЬЕФУ — где склон отвернулся от камеры сильнее ровной поверхности (как контур в аниме)
                            float3 Vv = normalize(_WorldSpaceCameraPos - i.positionWS);
                            half nvF = max(0.08h, (half)Vv.y);
                            half nv = (half)dot(normalize(i.normalWS), Vv);
                            half cont = 1.0h - smoothstep(0.45h, 0.7h, nv / nvF);
                            inkL = max(inkL, cont * (kd == 1 ? 0.45h : (kd == 2 ? 0.3h : 0.6h)) * (1.0h - smoothstep(0.88h, 0.96h, rL)));   // v23.51: у меренги контур мягче
                        }
                        col = lerp(col, lerp(col, shC, 0.7h), fsh * Lk * 0.6h);
                        col = lerp(col, hiC, flt * Lk * 0.55h);
                        col = lerp(col, shC * 0.5h, saturate(inkL) * Lk * 0.75h);                   // v23.15: обводка слоя
                        if (kd == 4)
                        {
                            // v23.43: МНОГОСЛОЙНЫЙ МОЛОЧНЫЙ ШОКОЛАД — 5 гладких налитых «языков», лежат друг на друге
                            // (те же формулы, что рельеф модели в C#: KindRelief case 4). У каждого слоя чуть свой тон,
                            // скруглённая кромка: со стороны света — тёплый блик, с другой — мягкая тень и тень на нижний слой,
                            // тонкий мягкий контур. Белых пятен, пор и капель больше нет.
                            float4 chl[5] = { float4(0.0, 0.0, 0.8, 0.0), float4(-0.13, 0.08, 0.6, 1.7), float4(0.15, -0.07, 0.47, 3.4),
                                              float4(-0.05, -0.13, 0.33, 5.1), float4(0.07, 0.1, 0.19, 6.8) };
                            half inrC = (1.0h - smoothstep(0.9, 0.97, rL)) * Lk;
                            half3 baseC = col;
                            half hlC = 0.0h, shSl = 0.0h, castC = 0.0h, inkC = 0.0h;
                            [unroll] for (int lc = 0; lc < 5; lc++)
                            {
                                float2 dq = qN - chl[lc].xy;
                                float aq = atan2(dq.y, dq.x);
                                float wk = min(1.0, chl[lc].z * 1.8);   // у маленьких слоёв край ровнее
                                float e = length(dq) - chl[lc].z - wk * (0.035 * sin(aq * 3.0 + chl[lc].w) + 0.02 * sin(aq * 5.0 + chl[lc].w * 1.3));
                                float awc = fwidth(e) + 0.002;
                                half inside = 1.0h - smoothstep(-awc, awc, e);
                                float2 nq = dot(dq, dq) > 1e-8 ? normalize(dq) : float2(0, 0);
                                half lit = dot(nq, ldir);
                                // тон слоя: чётные чуть светлее, нечётные чуть темнее (как налитые друг на друга порции)
                                half3 toneC = baseC * (lc % 2 == 0 ? 1.04h : 0.95h);
                                // новый слой сверху закрывает тени нижних
                                hlC *= 1.0h - inside; shSl *= 1.0h - inside; castC *= 1.0h - inside; inkC *= 1.0h - inside;
                                col = lerp(col, toneC, inside);
                                half rimIn = (1.0h - smoothstep(0.0, 0.06, -e)) * inside;            // скруглённая кромка слоя
                                hlC = max(hlC, rimIn * saturate(lit) * smoothstep(0.012, 0.035, -e));
                                shSl = max(shSl, rimIn * saturate(-lit + 0.15));
                                castC = max(castC, (1.0h - smoothstep(0.0, 0.07, e)) * step(0.0, e) * saturate(-lit + 0.3));
                                inkC = max(inkC, (1.0h - smoothstep(0.0, awc * 1.5 + 0.004, abs(e))) * 0.5h);
                            }
                            col = lerp(baseC, col, inrC);
                            col = lerp(col, lerp(col, shC, 0.55h), max(shSl, castC) * inrC * 0.55h);
                            col = lerp(col, saturate(col * 1.22h + half3(0.06h, 0.04h, 0.02h)), hlC * inrC * 0.75h);
                            col = lerp(col, shC * 0.7h, inkC * inrC * 0.35h);
                            // мягкий широкий глянец по верхнему слою (тёплый, не белый)
                            half glossC = (1.0h - smoothstep(0.08, 0.2, length(qN - ldir * 0.3 - float2(0.05, 0.05)))) * inrC;
                            col = lerp(col, saturate(col * 1.35h + half3(0.08h, 0.06h, 0.04h)), glossC * 0.35h);
                        }
                        if (kd == 6)
                        {
                            // v23.12: АНИМЕ-ВОДА: бирюзовый оттенок, к стенке глубже, светлая сетка каустики (две сетки, плывут в разные стороны)
                            half3 turq = half3(0.45h, 0.85h, 0.95h);
                            col = lerp(col, col * 0.4h + turq * 0.7h, 0.65h * Lk);
                            col = lerp(col, col * half3(0.55h, 0.8h, 0.95h), smoothstep(0.55h, 0.95h, rL) * 0.5h * Lk);
                            half net = 0.0h;
                            [unroll] for (int w6 = 0; w6 < 2; w6++)
                            {
                                float2 pw6 = i.uv.zw * (w6 == 0 ? 7.0 : 11.0) + _Time.y * (w6 == 0 ? float2(0.12, 0.07) : float2(-0.09, 0.11));
                                float2 iw = floor(pw6), fw6 = frac(pw6);
                                float f1 = 9.0, f2 = 9.0;
                                [unroll] for (int y6 = -1; y6 <= 1; y6++)
                                [unroll] for (int x6 = -1; x6 <= 1; x6++)
                                {
                                    float2 o6 = float2(x6, y6);
                                    float2 h6 = hash22(iw + o6);
                                    h6 = 0.5 + 0.4 * sin(_Time.y * 0.8 + 6.2831 * h6);
                                    float dd6 = length(o6 + h6 - fw6);
                                    if (dd6 < f1) { f2 = f1; f1 = dd6; } else if (dd6 < f2) f2 = dd6;
                                }
                                float e6 = f2 - f1;
                                float aw6 = fwidth(e6) + 0.01;
                                net = max(net, (1.0h - smoothstep(0.05 - aw6, 0.09 + aw6, e6)) * (w6 == 0 ? 1.0h : 0.55h));
                            }
                            col = lerp(col, half3(0.92h, 1.0h, 1.0h), net * 0.75h * Lk * (0.6h + 0.4h * light.shadowAttenuation));
                        }
                        half menL = smoothstep(0.9h, 0.92h, rL) * (1.0h - smoothstep(0.935h, 0.95h, rL)) * smoothstep(0.0h, 0.5h, toward);
                        col = lerp(col, lerp(half3(1, 1, 1), hiC, 0.3h), menL * Lk * 0.15h);           // v23.24: светлое кольцо мениска почти убрано
                        half wallInk = smoothstep(0.972h, 0.995h, rL);
                        col = lerp(col, shC * 0.6h, wallInk * Lk * 0.4h);                               // v23.24: контур у стенки тоньше и мягче
                        float2 qd = pdir * rL;
                        float2 perp = float2(-ldir.y, ldir.x);
                        float2 c1 = ldir * 0.45, c2 = ldir * 0.62 + perp * 0.12;
                        float alg = dot(qd - c1, perp), acr = dot(qd - c1, ldir);
                        // v23.14: блик-окно только там, где вещество блестит по природе (тесто и мороженое — матовые)
                        half hk = kd == 4 ? 0.3h : (kd == 7 || kd == 9) ? 0.95h : (kd == 0 ? 0.5h : (kd == 2 ? 0.4h : (kd == 1 ? 0.35h : (kd == 6 ? 0.7h : (kd == 8 ? 0.45h : 0.0h)))));
                        half sz = kd == 0 ? 1.3h : 1.0h;
                        float e1 = (alg * alg) / (0.0256 * sz * sz) + (acr * acr) / (0.0036 * sz * sz);
                        half win = 1.0h - smoothstep(0.85, 1.0, e1);
                        half dotH = 1.0h - smoothstep(0.03, 0.04, length(qd - c2));
                        col = lerp(col, half3(1, 1, 1), saturate(win + dotH) * hk * 0.7h * Lk * (0.5h + 0.5h * light.shadowAttenuation));
                    }
                }

                // ── блик и френель ──
                float3 Vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 H = normalize(light.direction + Vd);
                half nh = saturate(dot(nWS, H));
                half glintK = lerp(1.0h, 0.3h, cm) * lerp(1.0h, 0.15h, pk) * lerp(1.0h, 0.75h, tk) * lerp(1.0h, 0.15h, ik);
                half glint = smoothstep(_GlintSize, _GlintSize + 0.01h, nh) * glintK * (1.0h - 0.7h * _Anime);   // v23.5: в аниме — блики рисуют дуги и штрихи, «реалистичный» блеск тише
                half sheen = pow(nh, lerp(lerp(24.0h, 7.0h, pk), 34.0h, tk)) * _SheenStrength * lerp(1.0h, 1.6h, cm) * (1.0h + pk * 0.8h + tk * 1.4h);
                sheen *= 1.0h - 0.65h * _Anime;                                   // v23.5
                // сметана: мягкий глянец по гребням — «влажная» густая масса, а не матовое масло
                sheen += tk * pow(nh, 9.0h) * 0.08h;
                sheen *= lerp(1.0h, 0.35h, ik);                                  // мороженое матовое
                // морозные «снежинки»: голубые искры по мороженому, мерцают при повороте камеры
                if (ik > 0.001)
                {
                    float2 sgi = floor(i.uv.zw * 220.0);
                    half sp = 0.0h;                                               // v23.34: квадратные искры убраны (снежинки — частицами)
                    col += half3(0.55h, 0.75h, 1.0h) * sp * ik * 0.55h * saturate(ndl + 0.4h);
                }
                // иней: россыпь искр, которые мерцают при повороте камеры
                if (fk > 0.001)
                {
                    float2 sg = floor(i.uv.zw * 300.0);
                    half spark = step(0.985, hash21(sg + floor(dot(Vd, float3(13.1, 7.7, 5.3)) * 3.0)));
                    sheen += spark * fk * 1.2h * saturate(ndl + 0.3h);
                }
                half fres = pow(1.0h - saturate(dot(nWS, Vd)), _FresnelPower) * _FresnelStrength;
                fres *= 1.0h - 0.6h * _Anime;                                     // v23.5: без «пластикового» френеля
                fres *= 1.0h + fk;                                               // иней отливает по краям
                col += (_GlintColor.rgb * glint * _GlintStrength + sheen) * light.color * sh + _FresnelColor.rgb * fres;
                col = lerp(col, half3(1, 1, 1), fatHl * 0.85h);                  // v4: блик на жирных глазках
                // v23.4: блик-«окно» — две белые дуги у края напротив света (как на аниме-картинках жидкости)
                if (_Anime > 0.001h && _AnimeShine > 0.001h && _AnimeWallShadow > 0.001h)
                {
                    float2 dc2 = i.positionWS.xz - _AnimeCenter.xz;
                    float2 lx2 = light.direction.xz;
                    half opp = (dot(dc2, dc2) > 1e-8 && dot(lx2, lx2) > 1e-6) ? saturate(dot(normalize(dc2), -normalize(lx2))) : 0.0h;
                    half arcs = CCA_ShineArcs(i.uv.x, opp, atan2(dc2.y, dc2.x)) * _AnimeShine * _Anime * (1.0h - ik) * (1.0h - fk) * (0.5h + 0.5h * light.shadowAttenuation);
                    col = lerp(col, half3(1, 1, 1), arcs * 0.85h);
                }
                // v23.4: контурный свет — светлая полоса по дальнему краю (ступенькой, «аниме-рефлекс»)
                if (_Anime > 0.001h && _AnimeRim > 0.001h)
                {
                    half rimA = CCA_Rim(nWS, Vd, 0.3h) * _AnimeRim * _Anime;
                    col = lerp(col, lerp(half3(1, 1, 1), animeBase, 0.35h) * min(light.color, 1.2h), rimA * 0.35h);
                }

                half edge = saturate(i.color.a / max(0.02h, _EdgeSoftness));
                half a = saturate(max(max(max(lerp(_Opacity, 1.0h, cm), _Density), tk), max(ik, fk))) * edge;
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }

        // Глубина — для плотного молока (очередь непрозрачных): обводки, SSAO, туман по глубине
        // видят поверхность молока, а не дно под ним. Прозрачные лужи/струя сюда не попадают.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            struct DA { float4 positionOS : POSITION; };
            struct DV { float4 positionCS : SV_POSITION; };
            DV dv(DA v) { DV o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half df(DV i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex nv
            #pragma fragment nf
            struct NA { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct NV { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            NV nv(NA v)
            {
                NV o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 nf(NV i) : SV_Target { return half4(normalize(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
