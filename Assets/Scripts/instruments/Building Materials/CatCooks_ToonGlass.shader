// =============================================================
//  CatCooks_ToonGlass.shader
//  Мультяшное окно. URP 17 / Unity 6.
//
//  ═══ ГЛАВНОЕ РЕШЕНИЕ: ОКНО НЕПРОЗРАЧНОЕ ═══
//
//  В рисованной мультипликации окно почти никогда не показывает, что за ним.
//  Оно ЗАСВЕЧЕНО: плоская заливка цвета неба плюс две-три диагональные полосы
//  блика. Смотреть сквозь стекло зритель не должен — он должен понять «это
//  стекло» по одному силуэту.
//
//  Отсюда всё остальное. Раз прозрачность не нужна, шейдер по умолчанию идёт
//  В НЕПРОЗРАЧНОЙ ОЧЕРЕДИ: пишет глубину, не сортируется, не даёт овердро и не
//  требует Opaque Texture. Для фасада небоскрёба, где окон сотни, это разница
//  между «работает» и «не работает».
//
//  Настоящее преломление включается отдельным ключом и только там, где игрок
//  реально может заглянуть внутрь — обычно это окна текущего этажа.
//
//  ═══ ВСЁ ПРОЦЕДУРНО, БЕЗ ТЕКСТУР ═══
//
//  Полосы, грязь, разводы — формулами. Не из экономии, а потому что текстура
//  на фасаде из сотни окон означает сотню ОДИНАКОВЫХ окон. Процедурный блик
//  сдвигается по хешу от позиции объекта, и каждое окно получается своим
//  бесплатно.
//
//  ═══ ТРИ УРОВНЯ ПО РАССТОЯНИЮ ═══
//
//  Расстояние считается в вершинном шейдере, дальше по нему гасятся детали:
//
//    БЛИЗКО  — полосы, грязь, кромка, лёгкий градиент, опционально преломление
//    СРЕДНЕ  — полосы и градиент, грязь и кромка выключены
//    ДАЛЕКО  — плоская заливка одним цветом, ни одной октавы шума
//
//  Переход плавный, без щелчка: детали не пропадают, а растворяются. На
//  дальнем плане окно всё равно занимает несколько пикселей, и разницы не
//  видно — а фрагмент дешевеет в разы.
//
//  ═══ ДВА РЕЖИМА МАТЕРИАЛА ═══
//
//  ФАСАД (по умолчанию):  Blend One Zero, ZWrite On,  Queue Geometry
//  СКВОЗНОЕ:              Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off,
//                         Queue Transparent, ключ _REFRACTION_ON
//
//  Смешивание вынесено в свойства материала, поэтому оба режима живут на одном
//  шейдере и переключаются пресетом, а не отдельным файлом.
// =============================================================
Shader "CatCooks/Toon Glass"
{
    Properties
    {
        [Header(Base)]
        _SkyTop    ("Небо вверху",  Color) = (0.62, 0.86, 0.96, 1)
        _SkyBottom ("Небо внизу",   Color) = (0.86, 0.95, 0.99, 1)
        _GlassTint ("Оттенок стекла", Color) = (0.92, 0.98, 1.0, 1)

        _ViewShift ("Сдвиг неба от взгляда", Range(0, 1)) = 0.35

        [Header(Streaks)]
        _StreakColor ("Цвет блика", Color) = (1, 1, 1, 1)
        _StreakAngle ("Угол полос", Range(-90, 90)) = 38

        _Streak1Pos   ("Полоса 1 — центр", Range(-1, 1)) = -0.12
        _Streak1Width ("Полоса 1 — ширина", Range(0, 0.5)) = 0.16

        _Streak2Pos   ("Полоса 2 — центр", Range(-1, 1)) = 0.16
        _Streak2Width ("Полоса 2 — ширина", Range(0, 0.5)) = 0.06

        _StreakSoft ("Мягкость края полос", Range(0.001, 0.2)) = 0.012
        _StreakGain ("Яркость полос", Range(0, 3)) = 1.15

        [Header(Edges)]
        _RimColor ("Цвет кромки", Color) = (1, 1, 1, 1)
        _RimWidth ("Ширина кромки", Range(0, 0.3)) = 0.035
        _RimGain  ("Яркость кромки", Range(0, 2)) = 0.55

        [Header(Dirt)]
        _DirtAmount ("Грязь и разводы", Range(0, 1)) = 0.18
        _DirtScale  ("Крупность грязи", Range(0.5, 20)) = 5

        [Header(Lit windows)]
        _LitColor ("Цвет освещённого окна", Color) = (1, 0.86, 0.6, 1)
        _LitGain  ("Насколько ярче освещённое", Range(0, 2)) = 0.75

        [Header(Variation)]
        // Сдвиг рисунка бликов. Ставится скриптом на каждое окно отдельно —
        // без него фасад выглядит копипастой.
        _Phase ("Фаза (ставит скрипт)", Float) = 0

        // ВЫКЛЮЧЕНО ПО УМОЛЧАНИЮ, И ЭТО ВАЖНО.
        //
        // У меша без запечённых цветов Unity подставляет БЕЛЫЙ. Если читать
        // его как данные, белый означает «яркость на максимум, свет внутри
        // включён» — и обычный куб становится кремово-жёлтым сам по себе.
        //
        // Включай только на фасадах, где реально прогнан GlassPhaseBaker.
        [Toggle(_VERTEXCOLOR_VARIATION)] _UseVertexVariation
            ("Разброс из цветов вершин (после запекания)", Float) = 0

        [Header(Glint and reflection)]
        _GlintColor ("Цвет блика от солнца", Color) = (1, 1, 1, 1)
        _GlintSize  ("Размер блика", Range(0.5, 0.999)) = 0.965
        _GlintSoft  ("Мягкость блика", Range(0.001, 0.1)) = 0.012
        _GlintGain  ("Яркость блика", Range(0, 4)) = 1.6

        _FresnelPower ("Резкость френеля", Range(0.5, 8)) = 3.5
        _FresnelGain  ("Сила френеля", Range(0, 1.5)) = 0.45
        _FresnelColor ("Цвет френеля", Color) = (0.95, 0.99, 1, 1)

        // ЭТИ ДВА ОБЯЗАНЫ БЫТЬ ЗДЕСЬ, ХОТЬ И СКРЫТЫ.
        //
        // Инстансное свойство без объявления в Properties по умолчанию равно
        // НУЛЮ. Для _InstTint это означает умножение цвета на ноль — то есть
        // окно чёрное, пока его никто не выставил скриптом. Объявление здесь
        // задаёт значение по умолчанию, которое берётся, когда блок свойств
        // не назначен.
        [HideInInspector] _InstTint  ("Оттенок инстанса", Color) = (1, 1, 1, 1)
        [HideInInspector] _InstPhase ("Фаза инстанса", Float) = 0

        [Header(Distance LOD)]
        _NearDistance ("Полная детализация ближе, м", Float) = 12
        _FarDistance  ("Плоская заливка дальше, м",  Float) = 55
        _FarColor     ("Цвет на дальнем плане", Color) = (0.70, 0.88, 0.96, 1)

        [Header(Refraction (only see through mode))]
        [Toggle(_REFRACTION_ON)] _Refraction ("Включить преломление", Float) = 0
        _Distortion ("Сила преломления", Range(0, 0.2)) = 0.03
        _Frost      ("Матовость", Range(0, 1)) = 0.25
        _Opacity    ("Непрозрачность", Range(0, 1)) = 0.55

        [Header(Blend preset (see guide))]
        [HideInInspector] _SrcBlend ("Src", Float) = 1     // One
        [HideInInspector] _DstBlend ("Dst", Float) = 0     // Zero
        [HideInInspector] _ZWrite   ("ZWrite", Float) = 1  // On
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CatCooksToonGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            // Преломление — отдельным вариантом: в непрозрачном режиме код
            // выборки сцены не должен даже компилироваться, иначе он тянет за
            // собой требование Opaque Texture на весь проект.
            #pragma shader_feature_local_fragment _REFRACTION_ON

            // Читается и в вершинном, и во фрагментном — поэтому обычный
            // shader_feature_local, а не *_fragment.
            #pragma shader_feature_local _VERTEXCOLOR_VARIATION

            // Глобальный тумблер для слабых машин: гасит детали на всех
            // окнах разом, независимо от расстояния.
            #pragma multi_compile_local_fragment _ GLASS_CHEAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Нужен ради GetMainLight(): блик должен ехать за солнцем, а не
            // быть прибитым к экрану.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #ifdef _REFRACTION_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
        #endif

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyTop, _SkyBottom, _GlassTint;
                float  _ViewShift;
                float4 _StreakColor;
                float  _StreakAngle, _StreakSoft, _StreakGain;
                float  _Streak1Pos, _Streak1Width, _Streak2Pos, _Streak2Width;
                float4 _RimColor;
                float  _RimWidth, _RimGain;
                float  _DirtAmount, _DirtScale;
                float4 _LitColor;
                float  _LitGain;
                float4 _GlintColor, _FresnelColor;
                float  _GlintSize, _GlintSoft, _GlintGain;
                float  _FresnelPower, _FresnelGain;
                float  _Phase;
                float  _NearDistance, _FarDistance;
                float4 _FarColor;
                float  _Distortion, _Frost, _Opacity;
            CBUFFER_END

            // Фаза и оттенок — на инстанс: материал у всех окон ОДИН, а рисунок
            // блика у каждого свой. Иначе пришлось бы плодить материалы и
            // терять объединение вызовов.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstPhase)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                // x — детализация 0..1, y — фаза, zw — экранные UV
                float4 data       : TEXCOORD3;
                float4 screenPos  : TEXCOORD4;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i),                hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            /// Одна полоса блика. Плоская, с жёсткими краями — именно жёсткость
            /// делает её мультяшной. Мягкий градиент дал бы фотографический
            /// отблеск, а нам нужна нарисованная полоса.
            float Stripe(float p, float center, float halfWidth, float soft)
            {
                return 1.0 - smoothstep(halfWidth - soft, halfWidth + soft,
                                        abs(p - center));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS);

                o.positionCS = pos.positionCS;
                o.screenPos = ComputeScreenPos(pos.positionCS);
                o.uv = v.uv;
                o.normalWS = nrm.normalWS;
                o.viewDirWS = GetWorldSpaceNormalizeViewDir(pos.positionWS);
                o.color = v.color;

                // ── ДЕТАЛИЗАЦИЯ ПО РАССТОЯНИЮ ──
                // Считаем здесь, а не во фрагменте: расстояние одинаково для
                // всей грани окна, и на вершину это в тысячи раз дешевле.
                float dist = distance(GetCameraPositionWS(), pos.positionWS);
                float quality = saturate((_FarDistance - dist) /
                                         max(0.001, _FarDistance - _NearDistance));

            #ifdef GLASS_CHEAP
                quality = 0.0;
            #endif

                // ── ФАЗА ──
                // Три источника, по убыванию точности:
                //   1) _InstPhase — ставит скрипт на каждое окно;
                //   2) красный канал цвета вершин — если панели запечены в
                //      один меш и фазу проставили при генерации;
                //   3) хеш позиции объекта — грубый запасной вариант.
                //
                // СКЛАДЫВАЮТСЯ, а не заменяют друг друга: цвет вершин разводит
                // ПАНЕЛИ внутри одного меша, позиция объекта разводит ЭТАЖИ.
                // Сто этажей делят один запечённый меш, но одна и та же панель
                // на 3-м и на 40-м получает разный блик.
                float phase = UNITY_ACCESS_INSTANCED_PROP(Props, _InstPhase) + _Phase;
                phase += hash21(unity_ObjectToWorld._m03_m23 * 0.37) * 5.0;

                // Нейтральные значения по умолчанию. Именно они, а не цвет
                // вершин: у незапечённого меша он белый, и читать его как
                // данные означает «яркость на максимум, свет внутри включён».
                float bright = 1.0;
                float lit = 0.0;

            #ifdef _VERTEXCOLOR_VARIATION
                // Складывается с хешем позиции, а не заменяет его: цвет вершин
                // разводит ПАНЕЛИ внутри меша, позиция объекта разводит ЭТАЖИ.
                phase += v.color.r * 3.17;
                bright = 0.5 + v.color.g * 0.5;   // 0.5..1.0, белый = норма
                lit = v.color.b;
            #endif

                o.data = float4(quality, phase, bright, lit);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float quality = i.data.x;
                float phase = i.data.y;
                float2 uv = i.uv;

                float bright = i.data.z;
                float lit = i.data.w;

                // ── ДАЛЬНИЙ ПЛАН ──
                // Полностью плоская заливка. Выходим сразу, не считая ни
                // полос, ни шума: окно там занимает несколько пикселей, и
                // любая деталь всё равно схлопнется в один цвет.
                //
                // Запечённые яркость и «свет внутри» применяем ДАЖЕ ЗДЕСЬ:
                // именно они не дают дальнему фасаду выглядеть одной сплошной
                // плашкой. Стоят они умножения, то есть ничего.
                UNITY_BRANCH
                if (quality <= 0.001)
                {
                    float4 t0 = UNITY_ACCESS_INSTANCED_PROP(Props, _InstTint);
                    float3 far = lerp(_FarColor.rgb, _LitColor.rgb, lit * _LitGain);
                    return half4(far * bright * t0.rgb, 1.0);
                }

                // ── ФОН: НЕБО ──
                // Градиент едет от направления взгляда. Это дешёвая подделка
                // отражения: настоящего нет, но окна на фасаде перестают быть
                // одинаковыми плашками и «оживают», когда игрок движется.
                float g = saturate(uv.y + i.viewDirWS.y * _ViewShift);
                float3 col = lerp(_SkyBottom.rgb, _SkyTop.rgb, g) * _GlassTint.rgb;

                // Освещённое изнутри окно: подмешиваем тёплый цвет ДО полос,
                // чтобы блик оставался белым поверх тёплой заливки — как оно и
                // выглядит в жизни, когда стекло отражает небо, а светится
                // комната за ним.
                col = lerp(col, _LitColor.rgb, lit * _LitGain);
                col *= bright;

                // ── ПОЛОСЫ БЛИКА ──
                // Проекция UV на ось, повёрнутую на _StreakAngle. Одна
                // координата вместо двух — и полосы получаются идеально
                // параллельными при любом угле.
                float a = radians(_StreakAngle);
                float2 dir = float2(cos(a), sin(a));
                float p = dot(uv - 0.5, dir) + frac(phase) * 0.35 - 0.17;

                float s1 = Stripe(p, _Streak1Pos, _Streak1Width * 0.5, _StreakSoft);
                float s2 = Stripe(p, _Streak2Pos, _Streak2Width * 0.5, _StreakSoft);
                float streak = saturate(s1 + s2);

                col = lerp(col, _StreakColor.rgb, streak * _StreakGain * saturate(quality * 1.5));

                // ── ГРЯЗЬ ──
                // Только вблизи: на среднем плане её всё равно не видно, а
                // стоит она двух выборок шума.
                UNITY_BRANCH
                if (quality > 0.55 && _DirtAmount > 0.001)
                {
                    float n = noise2(uv * _DirtScale + phase);
                    n = n * 0.65 + noise2(uv * _DirtScale * 2.7 + 13.1) * 0.35;

                    // Разводы гуще у краёв — там, где стекло моют хуже всего.
                    float2 e = min(uv, 1.0 - uv);
                    float edgeBias = 1.0 - smoothstep(0.0, 0.35, min(e.x, e.y));

                    float dirt = n * lerp(0.35, 1.0, edgeBias) * _DirtAmount;
                    col *= 1.0 - dirt * (quality - 0.55) / 0.45;
                }

                // ── КРОМКА ──
                // Светлая рамка по периметру: у нарисованного стекла кромка
                // всегда ярче середины, потому что торец ловит свет.
                UNITY_BRANCH
                if (quality > 0.3 && _RimWidth > 0.001)
                {
                    float2 e = min(uv, 1.0 - uv);
                    float edge = min(e.x, e.y);
                    float rim = 1.0 - smoothstep(0.0, _RimWidth, edge);
                    col = lerp(col, _RimColor.rgb, rim * _RimGain * saturate((quality - 0.3) / 0.7));
                }

                // ── ФРЕНЕЛЬ ──
                // Под скользящим углом стекло отражает почти всё, в лоб —
                // почти ничего. Это самое узнаваемое свойство стекла, и без
                // него панель читается как крашеный лист, а не как окно.
                //
                // Считается всегда, даже на среднем плане: стоит одну степень,
                // а работает именно на дальних панелях фасада, которые мы
                // видим под углом.
                float ndv = saturate(dot(normalize(i.normalWS),
                                         normalize(i.viewDirWS)));
                float fres = pow(1.0 - ndv, _FresnelPower) * _FresnelGain;
                col = lerp(col, _FresnelColor.rgb, saturate(fres) * quality);

                // ── БЛИК ОТ СОЛНЦА ──
                // Резкий, с жёсткими краями — тоон-блик, а не PBR. Плавный
                // отблеск сделал бы стекло фотографическим и выбил бы его из
                // общей рисованной стилистики.
                //
                // Только вблизи: на фасаде за полсотни метров блик занимает
                // меньше пикселя и превращается в мерцающий шум при движении
                // камеры — хуже, чем его отсутствие.
                UNITY_BRANCH
                if (quality > 0.45 && _GlintGain > 0.001)
                {
                    Light sun = GetMainLight();
                    float3 h = normalize(sun.direction + normalize(i.viewDirWS));
                    float spec = saturate(dot(normalize(i.normalWS), h));

                    float glint = smoothstep(_GlintSize - _GlintSoft,
                                             _GlintSize + _GlintSoft, spec);

                    col += _GlintColor.rgb * glint * _GlintGain
                           * saturate((quality - 0.45) / 0.55);
                }

                float alpha = 1.0;

            #ifdef _REFRACTION_ON
                // ── СКВОЗНОЙ РЕЖИМ ──
                // Только для окон, к которым игрок реально подходит. Матовость
                // делается сдвигом координат по шуму — это НЕ настоящее
                // размытие: честное потребовало бы отдельного прохода
                // рендеринга через Render Feature, а на фасаде из сотни окон
                // такой проход не окупается ничем.
                float2 sUV = i.screenPos.xy / max(0.0001, i.screenPos.w);

                float2 off = (i.normalWS.xy) * _Distortion;
                float fn = noise2(sUV * 90.0 + phase) - 0.5;
                off += fn * _Frost * 0.06;

                float3 behind = SampleSceneColor(sUV + off);

                // Смешиваем ПО КАЧЕСТВУ: вдалеке преломление гаснет само, и
                // дальние окна не платят за то, чего не видно.
                float refractMix = (1.0 - _Opacity) * quality;
                col = lerp(col, lerp(behind, behind * _GlassTint.rgb, 0.6), refractMix);

                alpha = lerp(1.0, _Opacity, quality);
            #endif

                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstTint);
                return half4(col * tint.rgb, alpha * tint.a);
            }
            ENDHLSL
        }

        // Тень окна — плоская и дешёвая. Отдельный проход нужен, иначе стекло
        // не отбрасывает тень вообще и фасад выглядит нарисованным поверх.
        // ═══ ГЛУБИНА И НОРМАЛИ ═══
        //
        // Без этих двух проходов стекла НЕТ в текстурах глубины и нормалей,
        // которые URP собирает отдельно от основного кадра.
        //
        // Последствие неочевидное: экранная обводка, SSAO и всё, что строится
        // по этим текстурам, смотрит СКВОЗЬ стекло и обводит то, что за ним.
        // Выглядит это как просвечивающие контуры предметов на панели — при
        // том, что сама панель непрозрачная.
        //
        // Проходы дешёвые: без цвета, без освещения, только позиция и нормаль.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DVaryings DepthVert(DAttributes v)
            {
                DVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 DepthFrag(DVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct NAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            NVaryings NormalsVert(NAttributes v)
            {
                NVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 NormalsFrag(NVaryings i) : SV_Target
            {
                return half4(normalize(i.normalWS), 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct SAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct SVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 _LightDirection;

            SVaryings ShadowVert(SAttributes v)
            {
                SVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(v.normalOS);

                o.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(posWS, nrmWS, _LightDirection));
                return o;
            }

            half4 ShadowFrag(SVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
