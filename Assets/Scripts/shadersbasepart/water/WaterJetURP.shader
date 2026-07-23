Shader "Custom/WaterJetURP"
{
    // ═══════════════════════════════════════════════════════════════════
    // СТРУЯ ВОДЫ v2 — под референс "Fake Water / Toxic Waterfall":
    //   насыщенный ГОЛУБОЙ корпус (не пересвеченный белый!) +
    //   БЕЛЫЕ РВАНЫЕ ОШМЁТКИ ПЕНЫ (резкий step по шуму) +
    //   градиент вдоль струи (у выхода плотнее, к концу светлее) +
    //   светящийся ободок по краям (Fresnel).
    //
    // Ключевое отличие от v1: смешивание ALPHA-BLEND (не Additive!) для
    // ТЕЛА воды — поэтому цвет остаётся насыщенно-голубым на любом фоне и
    // не выгорает в белый. Светятся только ПЕНА и ободок (они добавляются
    // с HDR-яркостью поверх плотного тела) — и это то, что цепляет Bloom.
    // ═══════════════════════════════════════════════════════════════════
    Properties
    {
        [Header(Body Colors)]
        _DeepColor ("Вода: глубокий (у выхода)", Color) = (0.05, 0.35, 0.85, 0.9)
        _LightColor ("Вода: светлый (к концу)", Color) = (0.25, 0.75, 1.0, 0.8)
        _BodyOpacity ("Плотность тела воды", Range(0, 1)) = 0.85

        [Header(Foam HDR)]
        [HDR]_FoamColor ("Пена (белые ошмётки)", Color) = (3.5, 4.0, 4.2, 1)
        _FoamCutoff ("Порог пены (меньше = больше пены)", Range(0.1, 0.95)) = 0.5
        _FoamSoftness ("Мягкость краёв (мультяшно = 0.02-0.05)", Range(0.01, 0.5)) = 0.03

        [Header(Edge Glow HDR)]
        [HDR]_RimColor ("Ободок по краям", Color) = (1.2, 3.0, 4.5, 1)
        _RimPower ("Ширина ободка", Range(0.5, 8)) = 2.5

        [Header(Noise)]
        [NoScaleOffset]_MainNoise ("Шум крупный", 2D) = "white" {}
        _MainTiling ("Тайлинг крупный (X вокруг, Y вдоль)", Vector) = (1.5, 2.5, 0, 0)
        _MainSpeed ("Скорость крупный", Float) = 1.2
        [NoScaleOffset]_DetailNoise ("Шум мелкий", 2D) = "white" {}
        _DetailTiling ("Тайлинг мелкий", Vector) = (1.5, 2.5, 0, 0)
        _DetailSpeed ("Скорость мелкий", Float) = 2.2

        [Header(Fades)]
        _TopFade ("Фейд конца струи", Range(0, 1)) = 0.3

        [Header(Flow)]
        _Intensity ("Напор 0..1 (ставит скрипт)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            // ALPHA blend: тело воды ПЛОТНОЕ и насыщенное, не выгорает в белый.
            // Свечение обеспечивают HDR-цвета пены/ободка + Bloom.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
                float fogFactor    : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _LightColor, _FoamColor, _RimColor;
                float4 _MainTiling, _DetailTiling;
                float _BodyOpacity, _FoamCutoff, _FoamSoftness;
                float _RimPower, _MainSpeed, _DetailSpeed, _TopFade, _Intensity;
            CBUFFER_END

            TEXTURE2D(_MainNoise);   SAMPLER(sampler_MainNoise);
            TEXTURE2D(_DetailNoise); SAMPLER(sampler_DetailNoise);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float inten = saturate(_Intensity);
                float t = _Time.y;

                // ── Два слоя шума с разной скоростью (переливание).
                float2 uv1 = IN.uv * _MainTiling.xy   + float2(0, -t * _MainSpeed   * (0.4 + inten));
                float2 uv2 = IN.uv * _DetailTiling.xy + float2(0.13, -t * _DetailSpeed * (0.4 + inten));
                float n1 = SAMPLE_TEXTURE2D(_MainNoise,   sampler_MainNoise,   uv1).r;
                float n2 = SAMPLE_TEXTURE2D(_DetailNoise, sampler_DetailNoise, uv2).r;
                float noise = saturate(n1 * 0.65 + n2 * 0.55);

                // ── ТЕЛО ВОДЫ: градиент вдоль струи (uv.y: 0 выход → 1 конец),
                //    слегка промодулирован шумом (живые тёмные/светлые полосы).
                float along = IN.uv.y;
                float3 body = lerp(_DeepColor.rgb, _LightColor.rgb, along * 0.8 + noise * 0.25);

                // ── ПЕНА: резкий порог по шуму = белые РВАНЫЕ ошмётки, как на
                //    референсе. smoothstep с узкой полосой — чёткие края хлопьев.
                float foam = smoothstep(_FoamCutoff, _FoamCutoff + _FoamSoftness, noise);
                // Пены больше к концу струи (вода разбивается) и у самого выхода.
                float foamZone = saturate(along * 1.4) + saturate((0.12 - along) * 6.0) * 0.6;
                foam *= saturate(foamZone);

                // ── ОБОДОК: Fresnel по краям трубы.
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float rim = pow(1.0 - saturate(abs(dot(N, V))), _RimPower);

                // ── Сборка: плотное тело + HDR-пена + HDR-ободок.
                float3 col = body;
                col = lerp(col, _FoamColor.rgb, foam);      // ошмётки пены
                col += _RimColor.rgb * rim * 0.6;           // светящийся край

                // ── Альфа: тело плотное; конец струи растворяется.
                float a = _BodyOpacity;
                a = max(a, foam);                            // пена всегда видима
                if (_TopFade > 0.001)
                    a *= saturate((1.0 - along) / _TopFade);
                a *= inten;

                half4 outCol = half4(col, saturate(a));
                outCol.rgb = MixFog(outCol.rgb, IN.fogFactor);
                return outCol;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
