// ============================================================================
//  CatCooks / Anime Heap   (v23.6)
//
//  АНИМЕ-ШЕЙДЕР КУЧЕК: порошок (какао, мука), масло, паста, кристаллы (сахар).
//  Один шейдер, вид — ползунок «Вид». Цвет — из цвета вершин (цвет продукта).
//
//  Что рисует:
//   • ПАЛИТРА из 4 красок от цвета продукта: глубокая тень → тень → основа → свет.
//     Тень не просто темнее: светлые продукты (мука, сахар) — холодная сиреневая,
//     тёмные и тёплые (какао, шоколад, масло) — тёплая красноватая; в тенях насыщеннее.
//   • Граница тени «кистью» (рваная), на границе — тёплое свечение (как в кулинарном аниме).
//   • РЕЛЬЕФ — В МОДЕЛИ (v23.7): порошок — рыхлая горка из комьев с ложбинками и крошками; паста — витки ленты
//     один на другом, глянец по гребню каждого витка, на мастерке — валик через край с тяжёлой каплей.
//     Шейдер добавляет: масло — мазки; сахар — грани разных оттенков и искры-звёздочки.
//   • Блик — не реалистичный: резкое пятно с ореолом, рваный край, у пасты — «окно» с переплётом.
//   • Светлый край по силуэту, тёплый отражённый свет снизу в тени.
//   • ОБВОДКА: контур склонов + силуэт (вывернутая оболочка, ширина в пикселях) цветом глубокой тени.
//   • «Ложится»: низ кучки у пола темнее, кучка отбрасывает и принимает тени.
//  Меш кучки (FoodSubstanceMesh) кладёт в UV2: x — высота точки (0 низ … 1 макушка), y — радиус кучки;
//  у висящих капель пасты x < 0 (доля длины капли), UV3 — точка края полотна. Гребни слоёв пасты — в альфе цвета.
//  Без этих данных (другие меши) — узор по метрам, без затемнения низа.
// ============================================================================
Shader "CatCooks/Anime Heap"
{
    Properties
    {
        [Enum(Powder,0,Butter,1,Paste,2,Crystals,3)] _HKind ("Вид: порошок / масло / паста / кристаллы", Float) = 0
        [Header(Palitra)]
        _HShadeCool ("Тень светлых продуктов (холодная)", Color) = (0.62, 0.68, 1, 1)
        _HShadeWarm ("Тень тёмных и тёплых продуктов", Color) = (0.92, 0.5, 0.52, 1)
        _HShadeDepth ("Тень: насколько светлая", Range(0.2, 0.95)) = 0.62
        _HDeep ("Глубокая тень (низ, щели): насколько светлая", Range(0.1, 0.8)) = 0.34
        _HSat ("Насыщенность теней", Range(1, 2)) = 1.35
        _HLightLift ("Свет: светлее и теплее", Range(0, 1)) = 0.35
        _HAmbient ("Окружение сцены", Range(0, 1)) = 0.22
        [Header(Stupeni)]
        _HT1 ("Порог глубокой тени", Range(0, 1)) = 0.24
        _HT2 ("Порог тени", Range(0, 1)) = 0.5
        _HT3 ("Порог света", Range(0, 1)) = 0.8
        _HSoft ("Мягкость ступеней", Range(0.003, 0.15)) = 0.02
        _HBrush ("Кисть: рваная граница тени", Range(0, 0.4)) = 0.14
        _HBrushScale ("Кисть: мелкость", Range(1, 20)) = 5
        _HTermGlow ("Тёплое свечение на границе тени", Range(0, 1)) = 0.45
        [Header(Sloi)]
        _HLayers ("Слои и узор: сила", Range(0, 1.5)) = 0.65
        _HLayerCount ("Слои по высоте: сколько", Range(2, 16)) = 6
        _HDetail ("Узор: мелкость (1 — как задумано)", Range(0.3, 3)) = 1
        _HSpeck ("Крапинки двух цветов", Range(0, 1)) = 0.8
        _HContact ("Низ у пола темнее (ложится)", Range(0, 1)) = 0.7
        _HSmooth ("Порошок: сгладить свет по форме горки", Range(0, 1)) = 0
        _HDepthGrad ("Тень глубины: низ кучки темнее (мягко)", Range(0, 0.6)) = 0.15
        [HideInInspector] _HGrain ("", Float) = 0
        [Header(Blik)]
        _HSpec ("Блик", Range(0, 1)) = 0
        _HSpecSize ("Блик: размер (больше — меньше)", Range(0.8, 0.999)) = 0.965
        _HSpecBreak ("Блик: рваный край", Range(0, 1)) = 0.5
        _HSparkle ("Искры-звёздочки", Range(0, 1)) = 0.04
        _HRim ("Светлый край", Range(0, 1)) = 0.4
        _HBounce ("Отражённый свет в тени: цвет", Color) = (1, 0.62, 0.35, 1)
        _HBounceAmt ("Отражённый свет: сила", Range(0, 1)) = 0.3
        _HSss ("Паста: тёплый отсвет на тонких краях (SSS)", Range(0, 1)) = 0
        _HSssColor ("Паста: цвет отсвета", Color) = (1, 0.55, 0.25, 1)
        [Header(Obvodka)]
        _HInk ("Контур склонов: сила", Range(0, 1)) = 0.4
        _HInkWidth ("Контур склонов: ширина", Range(0.05, 0.6)) = 0.26
        _HOutline ("Силуэт: ширина, пикс", Range(0, 6)) = 2
        _HOutlineDark ("Силуэт: темнее", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _HKind;
            float4 _HShadeCool, _HShadeWarm, _HBounce;
            float _HShadeDepth, _HDeep, _HSat, _HLightLift, _HAmbient;
            float _HT1, _HT2, _HT3, _HSoft, _HBrush, _HBrushScale, _HTermGlow;
            float _HLayers, _HLayerCount, _HDetail, _HSpeck, _HContact, _HSmooth, _HGrain, _HDepthGrad;
            float _HSpec, _HSpecSize, _HSpecBreak, _HSparkle, _HRim, _HBounceAmt, _HSss;
            float4 _HSssColor;
            float _HInk, _HInkWidth, _HOutline, _HOutlineDark;
        CBUFFER_END

        static const float3 CCH_LUM = float3(0.299, 0.587, 0.114);

        float cch21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float2 cch22(float2 p)
        {
            float3 a = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            a += dot(a, a.yzx + 33.33);
            return frac((a.xx + a.yz) * a.zy);
        }
        float cchvn(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(cch21(i), cch21(i + float2(1, 0)), f.x), lerp(cch21(i + float2(0, 1)), cch21(i + float2(1, 1)), f.x), f.y);
        }
        float cchfbm(float2 p) { return cchvn(p) * 0.55 + cchvn(p * 2.03 + 7.1) * 0.3 + cchvn(p * 4.1 + 3.3) * 0.15; }

        // палитра из цвета продукта: свет / тень / глубокая тень
        void CCHPalette(float3 alb, out float3 hi, out float3 sh, out float3 deep)
        {
            float lum = dot(alb, CCH_LUM);
            float warm = saturate((alb.r - alb.b) * 2.2 + (0.5 - lum) * 0.8);
            float3 hue = lerp(_HShadeCool.rgb, _HShadeWarm.rgb, warm);
            sh = alb * hue * _HShadeDepth;
            float ls = dot(sh, CCH_LUM);
            sh = max(0.0, lerp(ls.xxx, sh, _HSat));
            deep = alb * hue * hue * _HDeep;
            float ld = dot(deep, CCH_LUM);
            deep = max(0.0, lerp(ld.xxx, deep, _HSat + 0.2));
            hi = alb * (1.0 + _HLightLift * 0.35) + float3(0.10, 0.07, 0.02) * _HLightLift * (0.4 + lum);
            // v23.9: у тёмных продуктов (какао, шоколад) свет заметно светлее — комья и складки читаются
            float dk = saturate(0.5 - lum) * 1.6;
            hi = lerp(hi, min(alb * 1.7 + float3(0.05, 0.03, 0.01), 1.0), dk);
            hi = min(hi, 1.0);
        }

        // v23.6: КАПЛИ ПАСТЫ, висящие с края полотна. Вершины капли: uv1.x = −(t + 0.002), t — 0 у края … 1 кончик;
        // uv2 — точка края (xz меша). Капля поворачивается вокруг края так, чтобы висеть ВНИЗ по миру
        // (мастерок наклонили — капля всё равно висит к полу), медленно тянется и покачивается.
        void CCHDrip(float3 posOS, float2 uv1, float2 uv2, inout float3 pw, inout float3 nw)
        {
            if (uv1.x > -0.001) return;
            float t = saturate(-uv1.x - 0.002);
            float3 rootW = TransformObjectToWorld(float3(uv2.x, 0.0, uv2.y));
            float3 rel = pw - rootW;
            float3 up = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
            float3 a = -up;
            float3 b = float3(0, -1, 0);
            float c = dot(a, b);
            float3 v = cross(a, b);
            float k = smoothstep(0.0, 0.5, t);
            if (c > -0.99)
            {
                float3 relG = rel * c + cross(v, rel) + v * (dot(v, rel) / (1.0 + c));
                float3 nG = nw * c + cross(v, nw) + v * (dot(v, nw) / (1.0 + c));
                rel = lerp(rel, relG, k);
                nw = normalize(lerp(nw, nG, k));
            }
            float ph = uv2.x * 37.0 + uv2.y * 53.0;
            float tm = _Time.y;
            float len = length(rel);
            rel.y *= 1.0 + 0.12 * sin(tm * 1.1 + ph) * t;
            float sw = sin(tm * 1.7 + ph) * 0.5 + sin(tm * 2.9 + ph * 1.3) * 0.25;
            rel.xz += float2(sw, cos(tm * 1.3 + ph) * 0.4) * len * 0.08 * t * t;
            pw = rootW + rel;
        }
        ENDHLSL

        // ───────────── основной проход ─────────────
        Pass
        {
            Name "AnimeHeap"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 nWS : TEXCOORD1;
                float3 upWS : TEXCOORD2;
                float3 qn : TEXCOORD3;        // координаты в «радиусах кучки»
                float3 nOS : TEXCOORD4;
                float2 hg : TEXCOORD5;        // x — высота 0..1, y — 1 если данные кучки есть
                float fog : TEXCOORD6;
                half4 color : COLOR;
            };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.nWS = TransformObjectToWorldNormal(v.normalOS, false);      // без нормалей длина 0
                CCHDrip(v.positionOS.xyz, v.uv1, v.uv2, o.positionWS, o.nWS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.upWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float R = v.uv1.y;
                float heapData = R > 1e-6 ? 1.0 : 0.0;
                o.qn = heapData > 0.5 ? v.positionOS.xyz / R : v.positionOS.xyz * 4.0;
                o.hg = float2(heapData > 0.5 ? (v.uv1.x < 0.0 ? 0.5 : saturate(v.uv1.x)) : 0.6, heapData);
                o.nOS = v.normalOS;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // крупные и мелкие комки (клетки Воронова, неровные глыбы); h — высота, g — наклон, id — случайное
            void Cells(float2 p, out float h, out float2 g, out float id)
            {
                float2 w = float2(cchvn(p * 0.7 + 3.1), cchvn(p * 0.7 + 8.7)) - 0.5;
                p += w * 0.9;
                float2 ip = floor(p), fp = frac(p);
                h = 0; g = 0; id = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 r = cch22(ip + o);
                    float2 v = o + 0.1 + r * 0.8 - fp;
                    float rad = 0.32 + 0.5 * r.y;
                    float d = length(v * float2(1.0, 0.7 + 0.6 * r.x));
                    float k = saturate(1.0 - d / rad);
                    float top = 0.35 + 0.65 * frac(r.x * 7.13 + r.y * 3.7);
                    float hk = smoothstep(0.0, 0.55, k) * top;
                    if (hk > h)
                    {
                        h = hk; id = r.x;
                        float edge = (k > 0.0 && k < 0.55) ? 2.4 * top / max(0.05, rad) : 0.15;
                        g = (-v / max(1e-4, length(v))) * edge;
                    }
                }
            }

            float Sparkle(float2 p, float3 vd, float density)
            {
                float2 ci = floor(p);
                float2 f = frac(p) - 0.5;
                float r = cch21(ci + 1.7);
                float tw = frac(r * 13.7 + dot(vd, float3(2.1, 1.3, 1.7)) * 1.5);   // мерцает, когда ходишь
                float on = step(1.0 - density, r) * smoothstep(0.7, 1.0, tw);
                f -= (cch22(ci) - 0.5) * 0.5;
                float star = saturate(0.002 / (abs(f.x * f.y) + 0.0004)) * saturate(1.0 - length(f) * 4.0);
                return star * on;
            }

            half4 frag(V i) : SV_Target
            {
                float3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                float3 nG = dot(i.nWS, i.nWS) > 0.25 ? normalize(i.nWS) : i.upWS;
                float3 nO = dot(i.nOS, i.nOS) > 0.25 ? normalize(i.nOS) : float3(0, 1, 0);
                float3 vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 q = i.qn * _HDetail;
                float hgt = i.hg.x;
                int kind = (int)round(_HKind);

                float3 n = nG;
                float cav = 0;
                float3 an = abs(nO);
                int ax = an.y > 0.6 ? 1 : (an.x > an.z ? 0 : 2);
                float2 p2 = ax == 1 ? q.xz : (ax == 0 ? q.zy : q.xy);

                if (kind == 0)
                {
                    // ── ПОРОШОК (v23.7): рыхлость — в самой модели (комья, ложбинки, крошки; ложбинки темнее в цвете вершин).
                    //    Шейдер — только две краски + обводка (по желанию свет можно сгладить по форме горки) ──
                    cav = (1.0 - i.color.a) * 0.9 * i.hg.y;                       // v23.8: ложбинки между комьями (из модели)
                    if (i.hg.y > 0.5 && _HSmooth > 0.001)
                    {
                        float hk = clamp(i.qn.y / max(hgt, 0.05), 0.2, 1.5);
                        float3 nD = TransformObjectToWorldNormal(float3(2.0 * hk * i.qn.x, 1.0, 2.0 * hk * i.qn.z), true);
                        n = normalize(lerp(n, nD, _HSmooth));
                    }
                }
                else if (kind == 1)
                {
                    // ── МАСЛО: мазки (валик и бороздка), поры среза ──
                    float2 d2 = float2(0.83, 0.55);
                    float2 w = float2(cchfbm(q.xz * 1.6 + 3.1), cchfbm(q.xz * 1.6 + 8.4)) - 0.5;
                    float u = dot(q.xz + w * 0.7, d2) * 9.0 + q.y * 4.0;
                    float s = sin(u);
                    float ridge = smoothstep(0.55, 0.9, s);
                    float groove = smoothstep(0.75, 0.97, -s);
                    alb *= 1.0 + ridge * 0.1 * _HLayers - groove * 0.16 * _HLayers;
                    cav = groove * 0.15;
                    float3 tWS = TransformObjectToWorldDir(float3(d2.x, 0, d2.y), true);
                    n = normalize(n - tWS * cos(u) * 0.22 * _HLayers);
                }
                else if (kind == 2)
                {
                    // ── ПАСТА: спираль молочных лент с тёмной каймой ──
                    float r = length(q.xz);
                    float a = atan2(q.z, q.x);
                    float w = cchfbm(q.xz * 1.8 + float2(1.3, 5.1)) - 0.5;
                    float u = r * 8.0 - a + w * 2.2 + q.y * 5.0;
                    float s = sin(u);
                    float ribbon = smoothstep(0.2, 0.5, s);
                    float ink = 1.0 - smoothstep(0.0, 0.12 + fwidth(s), abs(s - 0.2));
                    float3 milk = min(alb * 1.7 + float3(0.06, 0.03, 0.0), 1.0);
                    alb = lerp(alb, milk, ribbon * 0.55 * _HLayers);
                    alb *= 1.0 - ink * 0.28 * _HLayers;
                    float3 radO = float3(q.x, 0, q.z) / max(1e-4, r);
                    float3 radW = TransformObjectToWorldDir(radO, false);
                    n = normalize(n + radW * cos(u) * 0.08 * _HLayers);
                }
                else
                {
                    // ── КРИСТАЛЛЫ: грани разных оттенков ──
                    float3 c = floor(q * 22.0);
                    float r = cch21(c.xz + c.y * 7.31);
                    float3 tint = r < 0.3 ? float3(1, 1, 1) : (r < 0.55 ? float3(0.84, 0.92, 1.0) : (r < 0.8 ? float3(0.93, 0.87, 1.0) : float3(1.0, 0.96, 0.86)));
                    alb *= tint;
                }

                float3 hi, sh, deep;
                CCHPalette(alb, hi, sh, deep);

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float att = L.shadowAttenuation;
                float ndl = dot(n, L.direction);
                float x = saturate(ndl * 0.5 + 0.5);
                x += (cchvn(float2(q.x * _HBrushScale + q.y * 3.0, q.z * _HBrushScale)) - 0.5) * _HBrush;
                x = min(x, 0.38 + 0.62 * att);                                  // чужая тень — в краску «тень»
                float contact = (1.0 - smoothstep(0.0, 0.16, hgt)) * _HContact * i.hg.y;
                x -= cav * 0.35 + contact * 0.45;

                float3 col = deep;
                col = lerp(col, sh, smoothstep(_HT1 - _HSoft, _HT1 + _HSoft, x));
                col = lerp(col, alb, smoothstep(_HT2 - _HSoft, _HT2 + _HSoft, x));
                col = lerp(col, hi, smoothstep(_HT3 - _HSoft, _HT3 + _HSoft, x));
                float lum = dot(alb, CCH_LUM);
                float3 satC = saturate(lerp(lum.xxx, alb, 1.8));
                float tgw = (x - _HT2) / 0.07;
                col += satC * exp(-tgw * tgw) * _HTermGlow * 0.35;
                float3 lc = min(L.color, 1.4);
                col = col * lerp(float3(1, 1, 1), lc, 0.7) + alb * SampleSH(nG) * _HAmbient;
                col *= 1.0 - _HDepthGrad * (1.0 - smoothstep(0.0, 0.5, hgt)) * i.hg.y;      // тень глубины (слой «Умножение»)

                // светлый край и тёплый отражённый свет
                float nvG = saturate(dot(nG, vd));
                float fres = 1.0 - nvG;
                float rim = smoothstep(0.6, 0.66, fres) * saturate(dot(nG, L.direction) + 0.3) * att;
                col = lerp(col, min(hi * 1.1 + 0.08, 1.0), rim * _HRim);
                float bnc = saturate(-dot(nG, L.direction)) * smoothstep(0.3, 0.8, fres);
                col += _HBounce.rgb * alb * bnc * _HBounceAmt;

                // блик: резкое пятно + ореол, рваный край; у пасты — «окно» с переплётом
                if (_HSpec > 0.001)
                {
                    float3 H = normalize(L.direction + vd);
                    float ndh = saturate(dot(n, H));
                    float s = ndh + (cchvn(float2(q.x * 6.0 + q.y * 3.0, q.z * 6.0)) - 0.5) * 0.08 * _HSpecBreak;
                    float fw = fwidth(s) + 0.002;
                    float core = smoothstep(_HSpecSize - fw, _HSpecSize + fw, s);
                    float halo = smoothstep(_HSpecSize - 0.03 - fw, _HSpecSize - 0.024 + fw, s);
                    if (kind == 2)
                    {
                        float st = frac((q.x + q.z) * 0.7 * 3.0);
                        float gap = smoothstep(0.42, 0.46, st) * (1.0 - smoothstep(0.54, 0.58, st));
                        core *= 1.0 - gap * 0.9 * saturate(_HLayers * 2.0);          // v23.11: переплёт — только если узор включён
                    }
                    float3 specC = lerp(hi, 1.0, 0.75) * min(L.color, 1.0);
                    col = lerp(col, min(hi * 1.15, 1.0), halo * (1.0 - core) * _HSpec * 0.35 * att);
                    col = lerp(col, specC, core * _HSpec * att);
                }
                if (_HSparkle > 0.001)
                {
                    float sp = Sparkle(float2(q.x * 18.0 + q.y * 7.0, q.z * 18.0), vd, _HSparkle * 0.25) * saturate(ndl + 0.2);
                    col = lerp(col, 1.0, sp);
                }
                if (kind == 2 && _HSss > 0.001)
                {
                    // v23.11: подповерхностное рассеивание — тёплый карамельный отсвет на тонких краях (масса съедобная, не «пластилин»)
                    float sss = pow(1.0 - nvG, 2.5) * (0.6 + 0.4 * saturate(-ndl));
                    col += _HSssColor.rgb * alb * sss * _HSss * 1.5;
                }
                col *= 1.0 - contact * 0.25;
                if (kind == 1 || kind == 2)          // v23.9: и валики масла
                {
                    // гребни слоёв пасты (меш кладёт гребень в альфу цвета: 1 − гребень) — белая полоска глянца вдоль ленты
                    float crest = (1.0 - i.color.a) * i.hg.y;                  // только у мешей кучек (не у поверхности в таре)
                    float stk = smoothstep(0.45, 0.8, crest) * smoothstep(-0.1, 0.3, ndl) * att;
                    col = lerp(col, min(hi * 0.4 + 0.62, 1.0), stk * 0.85);
                }

                // контур склонов (цветом глубокой тени, сглажен по экрану)
                float lw = fwidth(nvG) * 1.2 + 1e-4;
                float inkL = 1.0 - smoothstep(_HInkWidth - lw, _HInkWidth + lw, nvG);
                col = lerp(col, deep * 0.7, inkL * _HInk);

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ───────────── силуэт: вывернутая оболочка, ширина в пикселях ─────────────
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float fog : TEXCOORD0; };

            V vert(A v)
            {
                V o;
                float3 nO = v.normalOS;
                if ((int)round(_HKind) == 3 && v.uv1.y > 1e-6)             // кубики сахара: наружу от центра кучки, без разрывов
                    nO = normalize(nO * 0.4 + normalize(v.positionOS.xyz + float3(0, 1e-4, 0)) * 0.6);
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(nO, false);
                CCHDrip(v.positionOS.xyz, v.uv1, v.uv2, pw, nw);
                float4 pc = TransformWorldToHClip(pw);
                float2 d = TransformWorldToHClipDir(nw).xy;
                float len = length(d);
                d = len > 1e-5 ? d / len : float2(0, 0);
                // v23.24: далеко — заметно тоньше и совсем пропадает (иначе мелкие комья превращаются в «каракули»)
                float wpx = _HOutline * saturate(3.0 / max(pc.w, 0.01)) * (1.0 - smoothstep(6.0, 14.0, pc.w));
                pc.xy += d * wpx * pc.w * 2.0 / _ScreenParams.xy;
                o.positionCS = pc;
                o.color = v.color;
                o.fog = ComputeFogFactor(pc.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                if (_HOutline <= 0.001) discard;
                float3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                float3 hi, sh, deep;
                CCHPalette(alb, hi, sh, deep);
                float3 c = deep * (1.0 - 0.6 * _HOutlineDark);
                // v23.24: у светлых продуктов (мука, сахар) контур тёплый серо-коричневый, а не синий
                float lumO = dot(alb, CCH_LUM);
                float3 warmInk = alb * float3(0.55, 0.47, 0.4) * (1.0 - 0.5 * _HOutlineDark);
                c = lerp(c, warmInk, smoothstep(0.45, 0.7, lumO));
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        // ───────────── тени (кучка отбрасывает тень — «ложится») ─────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct V { float4 positionCS : SV_POSITION; };

            V vert(A v)
            {
                V o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = SafeNormalize(TransformObjectToWorldNormal(v.normalOS, false));
                CCHDrip(v.positionOS.xyz, v.uv1, v.uv2, pw, nw);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 ld = normalize(_LightPosition - pw);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 pc = TransformWorldToHClip(ApplyShadowBias(pw, nw, ld));
                #if UNITY_REVERSED_Z
                    pc.z = min(pc.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    pc.z = max(pc.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = pc;
                return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        // ───────────── глубина и нормали (SSAO, глубина сцены) ─────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A v)
            {
                V o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = float3(0, 1, 0);
                CCHDrip(v.positionOS.xyz, v.uv1, v.uv2, pw, nw);
                o.positionCS = TransformWorldToHClip(pw);
                return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct V { float4 positionCS : SV_POSITION; float3 nWS : TEXCOORD0; };
            V vert(A v)
            {
                V o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS, false);
                CCHDrip(v.positionOS.xyz, v.uv1, v.uv2, pw, nw);
                o.positionCS = TransformWorldToHClip(pw);
                o.nWS = nw;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = dot(i.nWS, i.nWS) > 1e-6 ? normalize(i.nWS) : float3(0, 1, 0);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    float2 rem = saturate(oct * 0.5 + 0.5);
                    return half4(PackFloat2To888(rem), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
