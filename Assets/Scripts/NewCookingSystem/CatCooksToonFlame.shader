// ============================================================================
//  CatCooks / Toon Flame
//
//  Мультяшная струя газового пламени — один слой (конус, ToonGasFlame строит 3 слоя:
//  оболочка, середина, ядро). Меш — единичный конус вдоль +Z: uv.y = 0 у сопла, 1 на кончике.
//
//  • Цвет — ТРИ ступени вдоль струи (A у сопла → B → C на кончике) с жёсткими краями;
//    граница ступеней «гуляет» по шуму — языки.
//  • Кончик рвётся на языки (растворение по шуму), вершины «дышат» и виляют.
//  • Середина ярче края (ядро), лёгкий ободок; пересвет (Glow) — для Bloom.
//  • Смешивание premultiplied: Additive 0 — плотное мультяшное, 1 — светящееся.
// ============================================================================
Shader "CatCooks/Toon Flame"
{
    Properties
    {
        [HDR] _ColA ("Цвет у сопла", Color) = (0.7, 0.9, 1, 1)
        [HDR] _ColB ("Цвет середины", Color) = (0.3, 0.5, 1, 1)
        [HDR] _ColC ("Цвет кончика", Color) = (1, 0.55, 0.2, 1)
        _Band1 ("Ступень A→B", Range(0, 1)) = 0.3
        _Band2 ("Ступень B→C", Range(0, 1)) = 0.7
        _BandSoft ("Мягкость ступеней", Range(0.001, 0.3)) = 0.04
        _BandNoise ("Языки на ступенях", Range(0, 0.6)) = 0.18
        _Dissolve ("Кончик рвётся с", Range(0, 1)) = 0.6
        _Glow ("Яркость (пересвет)", Range(0.5, 6)) = 1.6
        _Core ("Ядро ярче к середине", Range(0, 2)) = 0.6
        _Alpha ("Плотность", Range(0, 1)) = 0.95
        _Additive ("Свечение (0 — плотное, 1 — аддитив)", Range(0, 1)) = 0.35
        _NoiseScale ("Шум: размер", Range(0.5, 12)) = 3.5
        _Speed ("Шум: скорость вдоль струи", Range(0, 20)) = 6
        _Wobble ("Вершины дышат", Range(0, 0.6)) = 0.18
        _Sway ("Кончик виляет", Range(0, 0.6)) = 0.12
        _Seed ("Зерно", Float) = 0
        _Holes ("v15: дыры в языках (как в Zelda)", Range(0, 1)) = 0.35
        [HDR] _EdgeCol ("v15: кайма дыр и края", Color) = (1, 0.4, 0.1, 1)
        _EdgeWidth ("v15: ширина каймы", Range(0, 0.2)) = 0.06
        _Distort ("v15: колыхание цвета", Range(0, 0.4)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Back
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "ToonFlame"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColA, _ColB, _ColC;
                half _Band1, _Band2, _BandSoft, _BandNoise, _Dissolve, _Glow, _Core, _Alpha, _Additive;
                float _NoiseScale, _Speed, _Wobble, _Sway, _Seed;
                half _Holes, _EdgeWidth, _Distort;
                half4 _EdgeCol;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 np : TEXCOORD3;          // точка для шума (вдоль струи бежит)
            };

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i + float3(0, 0, 0)), hash31(i + float3(1, 0, 0)), f.x),
                                 lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x),
                                 lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            V vert(A v)
            {
                V o;
                float t = v.uv.y;
                float time = _Time.y + _Seed * 13.7;
                float3 p = v.positionOS.xyz;
                // шум бежит от сопла к кончику
                float3 np = float3(v.uv.x * 6.2831 * 0.6, t * _NoiseScale - time * _Speed, _Seed);
                float n = vnoise(np) - 0.5;
                // дышит наружу (сильнее к кончику)
                float2 radial = normalize(v.normalOS.xy + 1e-5);
                p.xy += radial * n * _Wobble * (0.3 + t) * length(p.xy) * 2.0;
                // кончик виляет
                float sw = t * t * _Sway;
                p.x += (sin(time * 9.1 + t * 4.0) * 0.6 + sin(time * 17.3 + t * 7.0) * 0.4) * sw;
                p.y += (sin(time * 7.7 + t * 5.0 + 1.3) * 0.6 + sin(time * 13.9 + t * 3.0) * 0.4) * sw;
                // длина «дышит»
                p.z *= 1.0 + (vnoise(float3(time * 3.0, _Seed, 0)) - 0.5) * 0.12 * t;
                float3 ws = TransformObjectToWorld(p);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.np = np;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float t = i.uv.y;
                // v15: колыхание — шум сдвигает «где мы» вдоль струи и по кругу
                float dsx = vnoise(i.np * 1.3 + float3(4.1, 0, 2.0)) - 0.5;
                i.np.x += dsx * _Distort * 6.0;
                t = saturate(t + dsx * _Distort * 0.5 * t);
                float n1 = vnoise(i.np * float3(1, 1, 1));
                float n2 = vnoise(i.np * 2.13 + 7.1);
                float n = n1 * 0.65 + n2 * 0.35;
                // ступени вдоль струи (граница гуляет по шуму — языки)
                float s = t + (n - 0.5) * _BandNoise * 2.0;
                half k1 = smoothstep(_Band1 - _BandSoft, _Band1 + _BandSoft, s);
                half k2 = smoothstep(_Band2 - _BandSoft, _Band2 + _BandSoft, s);
                half3 col = lerp(lerp(_ColA.rgb, _ColB.rgb, k1), _ColC.rgb, k2);

                // ядро: середина ярче, край чуть темнее (объём без света)
                half3 nrm = normalize(i.normalWS);
                half3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndv = saturate(abs(dot(nrm, vdir)));
                col *= 1.0 + _Core * ndv * ndv;

                // кончик рвётся на языки
                half dis = saturate((t - _Dissolve) / max(1e-3, 1.0 - _Dissolve));
                half a = _Alpha * step(dis * 1.05, n * 0.9 + 0.08);
                // v15: дыры в языках (растут к кончику), края дыр и кончика — тёмно-оранжевая кайма
                float hn = vnoise(i.np * 1.7 + float3(3.1, 7.7, 1.3)) * 0.7 + vnoise(i.np * 3.9 + 9.0) * 0.3;
                float carve = saturate((t - 0.3) / 0.7) * _Holes;
                a *= step(carve, hn);
                half rimH = 1.0 - smoothstep(_EdgeWidth * 0.3, _EdgeWidth, hn - carve);
                half rimT = 1.0 - smoothstep(_EdgeWidth * 0.3, _EdgeWidth, (n * 0.9 + 0.08) - dis * 1.05);
                half rim = saturate(max(rimH * step(0.001, carve), rimT * step(0.001, dis)));
                col = lerp(col, _EdgeCol.rgb, rim);
                // край силуэта чуть прозрачнее — не «пластиковый»
                a *= lerp(0.55, 1.0, smoothstep(0.0, 0.35, ndv));
                // у самого сопла — не резко
                a *= smoothstep(0.0, 0.06, t);
                clip(a - 0.01);

                half3 rgb = col * _Glow * a;
                return half4(rgb, a * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
