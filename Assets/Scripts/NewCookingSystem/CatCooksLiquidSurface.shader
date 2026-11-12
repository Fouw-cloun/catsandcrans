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
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
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
                    icePit = smoothstep(0.2, 0.05, c0) * ik;                      // дырочки (крупнее и глубже — рыхлее)
                    float2 gp = i.uv.zw * 210.0;
                    float g0 = vnoise(gp), gx = vnoise(gp + float2(0.4, 0)), gz = vnoise(gp + float2(0, 0.4));
                    float3 bump = float3(-(cx - c0) * 2.2 - (gx - g0) * 0.7, 0, -(cz - c0) * 2.2 - (gz - g0) * 0.7);
                    nWS = normalize(nWS + bump * ik * 1.35);
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
                    half speck = step(0.992, hash21(sc + 7.3)) * ik;
                    col = lerp(col, col * half3(0.45h, 0.36h, 0.3h), speck * 0.6h);
                }
                // ── иней: холодный отлив и искры ──
                if (fk > 0.001)
                {
                    col = lerp(col, col * half3(0.92h, 0.97h, 1.06h) + 0.04h, fk * 0.6h);
                }

                // ── мениск ──
                half men = smoothstep(0.84, 1.0, i.uv.x) * _Meniscus;
                col *= 1.0h + men * 0.14h;
                col *= 1.0h - smoothstep(0.985, 1.0, i.uv.x) * 0.22h * _Meniscus;

                // ── прожилки струи ──
                if (_FlowStreaks > 0.001)
                {
                    float st = vnoise(float2(i.uv.x * 7.0, i.uv.y * _StreakScale));
                    col *= 1.0h + (st - 0.5h) * 0.35h * _FlowStreaks;
                }

                // ── блик и френель ──
                float3 Vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 H = normalize(light.direction + Vd);
                half nh = saturate(dot(nWS, H));
                half glintK = lerp(1.0h, 0.3h, cm) * lerp(1.0h, 0.15h, pk) * lerp(1.0h, 0.75h, tk) * lerp(1.0h, 0.15h, ik);
                half glint = smoothstep(_GlintSize, _GlintSize + 0.01h, nh) * glintK;
                half sheen = pow(nh, lerp(lerp(24.0h, 7.0h, pk), 34.0h, tk)) * _SheenStrength * lerp(1.0h, 1.6h, cm) * (1.0h + pk * 0.8h + tk * 1.4h);
                // сметана: мягкий глянец по гребням — «влажная» густая масса, а не матовое масло
                sheen += tk * pow(nh, 9.0h) * 0.08h;
                sheen *= lerp(1.0h, 0.35h, ik);                                  // мороженое матовое
                // морозные «снежинки»: голубые искры по мороженому, мерцают при повороте камеры
                if (ik > 0.001)
                {
                    float2 sgi = floor(i.uv.zw * 220.0);
                    half sp = step(0.988, hash21(sgi + 3.1 + floor(dot(Vd, float3(9.7, 4.3, 6.1)) * 3.0)));
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
                fres *= 1.0h + fk;                                               // иней отливает по краям
                col += (_GlintColor.rgb * glint * _GlintStrength + sheen) * light.color * sh + _FresnelColor.rgb * fres;
                col = lerp(col, half3(1, 1, 1), fatHl * 0.85h);                  // v4: блик на жирных глазках

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
