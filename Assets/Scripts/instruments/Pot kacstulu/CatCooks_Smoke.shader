// =============================================================
//  CatCooks_Smoke.shader
//  Дым над костром. URP 17 / Unity 6.
//
//  ═══ ТОТ ЖЕ ПРИЁМ, ЧТО У ОГНЯ ═══
//
//  Один меш-оболочка вместо частиц: конус или вытянутый цилиндр над кострищем.
//  Форма клубов — из шума, а не из геометрии.
//
//  Но дым ДОРОЖЕ огня, хотя выглядит проще, и вот почему: он альфа-смешанный,
//  а не аддитивный. Аддитивный огонь можно рисовать в любом порядке, дым —
//  нельзя, он затемняет фон. Плюс дымовая оболочка крупная и занимает много
//  пикселей. Поэтому здесь ВСЕГО ОДНА ОКТАВА шума и никаких языков.
//
//  ═══ ДЫМ ГУЩЕ НА УГЛЯХ, ЧЕМ НА ПЛАМЕНИ ═══
//
//  Контринтуитивно, но верно: хорошо горящий костёр дымит мало — топливо
//  сгорает полностью. Валит дым от тлеющего, сырого и затухающего. Плотность
//  гонит FireVisualController, и она ОБРАТНА мощности огня.
//
//  Побочный эффект приятный: пока костёр в порядке, обзор чистый; как только
//  просел — площадку затягивает, и игрок замечает это боковым зрением, не
//  глядя на приборы.
//
//  ═══ ТРЕБОВАНИЯ К МЕШУ ═══
//  Конус без крышек, пивот в основании, локальная Y вверх, высота 1.
//  Развёртка: U по окружности, V по высоте.
// =============================================================
Shader "CatCooks/Smoke"
{
    Properties
    {
        [Header(Look)]
        _ColorNear ("Цвет у огня",  Color) = (0.28, 0.26, 0.24, 1)
        _ColorFar  ("Цвет вверху",  Color) = (0.62, 0.62, 0.64, 1)

        _Density   ("Плотность 0..1", Range(0, 1)) = 0.4
        _Softness  ("Мягкость клубов", Range(0.05, 1)) = 0.45

        [Header(Motion)]
        _RiseSpeed  ("Скорость подъёма", Range(0, 3)) = 0.35
        _NoiseScale ("Крупность клубов", Range(0.5, 8)) = 2.2
        _Drift      ("Снос ветром (XZ)", Vector) = (0.15, 0, 0.05, 0)
        _Swirl      ("Закрутка", Range(0, 2)) = 0.5

        [Header(Fade)]
        _TopFade    ("Растворение вверху", Range(0.05, 1)) = 0.55
        _BottomFade ("Растворение внизу",  Range(0.0, 0.6)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent-10"   // дым ЗА огнём
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CatCooksSmoke"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorNear, _ColorFar, _Drift;
                float  _Density, _Softness;
                float  _RiseSpeed, _NoiseScale, _Swirl;
                float  _TopFade, _BottomFade;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _InstDensity)
                UNITY_DEFINE_INSTANCED_PROP(float, _InstPhase)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 data       : TEXCOORD1;  // h01, phase, density
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

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float h01 = saturate(v.positionOS.y);
                float density = UNITY_ACCESS_INSTANCED_PROP(Props, _InstDensity);
                float phase   = UNITY_ACCESS_INSTANCED_PROP(Props, _InstPhase);

                // Снос ветром растёт с высотой квадратично: столб у земли
                // стоит вертикально и заваливается тем сильнее, чем выше.
                // Линейный снос выглядит наклонной палкой.
                float3 posOS = v.positionOS.xyz;
                posOS.xz += _Drift.xz * (h01 * h01) * 4.0;

                o.positionCS = TransformObjectToHClip(posOS);
                o.uv   = v.uv;
                o.data = float3(h01, phase, density);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float h01     = i.data.x;
                float t       = _Time.y + i.data.y;
                float density = i.data.z * _Density;

                float2 uv = i.uv;
                uv.x += h01 * _Swirl;

                float n = noise2(float2(uv.x * _NoiseScale,
                                        uv.y * _NoiseScale - t * _RiseSpeed));

                // Клубы: порог по плотности. Чем гуще дым, тем больше шума
                // проходит, тем плотнее столб.
                float a = smoothstep(1.0 - density - _Softness,
                                     1.0 - density + _Softness, n);

                // Растворяем сверху и снизу — иначе видны торцы конуса.
                a *= smoothstep(1.0, 1.0 - _TopFade, h01);
                a *= smoothstep(0.0, _BottomFade, h01);

                clip(a - 0.01);

                // Вверху дым светлеет: остывает и рассеивается.
                float3 col = lerp(_ColorNear.rgb, _ColorFar.rgb, h01);

                return half4(col, a * 0.75);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
