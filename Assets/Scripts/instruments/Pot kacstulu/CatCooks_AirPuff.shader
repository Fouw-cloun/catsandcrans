// =============================================================
//  CatCooks_AirPuff.shader
//  Клуб выдуваемого воздуха. URP 17 / Unity 6.
//
//  ═══ ГЛАВНЫЙ ПРИЁМ: РАСПАД, А НЕ ЗАТУХАНИЕ ═══
//
//  Стандартный материал частиц гасит альфу равномерно — клякса просто тускнеет
//  целиком и выглядит мутным пятном. Настоящий пар так себя не ведёт: он
//  РАЗРЫВАЕТСЯ на клочья, которые тают по отдельности.
//
//  Здесь альфа частицы (та самая кривая Color over Lifetime, которую задаёт
//  WindParticles) используется НЕ как прозрачность, а как ПОРОГ РАСТВОРЕНИЯ
//  против шума. Клуб рождается из ничего, набирает плотность, потом расползается
//  дырами и распадается на обрывки.
//
//  Разница в одну строку кода и огромная в том, как это читается.
//
//  ═══ РАСКАЛЁННАЯ КРОМКА ═══
//
//  По краю растворения идёт подсветка: там, где шум почти дошёл до порога,
//  пиксель ярче. Без неё клочья выглядят вырезанными ножницами; с ней у них
//  появляется светящийся ободок, как у настоящего пара на солнце.
//
//  ═══ ПОЧЕМУ АЛЬФА-СМЕШИВАНИЕ, А НЕ АДДИТИВНОЕ ═══
//
//  Пар не светится, он РАССЕИВАЕТ свет — то есть закрывает фон, а не
//  складывается с ним. Аддитивный режим уместен для огня и совершенно неуместен
//  здесь: белый пар на светлом фоне просто исчез бы.
//
//  ═══ ПРИМЕНЕНИЕ ═══
//
//  Материал на этом шейдере назначается в поле Material компонента
//  WindParticles. Если оставить поле пустым, он подхватится автоматически.
//
//  Хорошо работает в режиме Stretched Billboard: клуб вытягивается по движению
//  и читается как стремительный порыв, а не как облачко.
// =============================================================
Shader "CatCooks/Air Puff"
{
    Properties
    {
        [Header(Look)]
        _Color     ("Цвет пара", Color) = (1, 1, 1, 1)
        _EdgeColor ("Цвет кромки", Color) = (1, 1, 1, 1)
        _EdgeGlow  ("Яркость кромки", Range(0, 4)) = 1.4
        _EdgeWidth ("Ширина кромки", Range(0.01, 0.5)) = 0.14

        [Header(Shape)]
        _SoftEdge  ("Мягкость круга", Range(0.01, 1)) = 0.45
        _Density   ("Плотность", Range(0.1, 3)) = 1.1

        [Header(Noise)]
        _NoiseScale ("Крупность клочьев", Range(0.5, 12)) = 3.2
        _NoiseSpeed ("Скорость шевеления", Range(0, 4)) = 0.9
        _Detail     ("Мелкая рябь", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "CatCooksAirPuff"
            Tags { "LightMode" = "UniversalForward" }

            // Пар РАССЕИВАЕТ свет, а не излучает: обычная альфа, не аддитив.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _EdgeColor;
                float  _EdgeGlow, _EdgeWidth;
                float  _SoftEdge, _Density;
                float  _NoiseScale, _NoiseSpeed, _Detail;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // из Color over Lifetime
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 posWS      : TEXCOORD1;
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
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);

                o.positionCS = p.positionCS;
                o.posWS = p.positionWS;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // ── круглая основа ──
                // Клуб должен быть круглым, а не квадратным по границе спрайта.
                float2 c = i.uv - 0.5;
                float d = length(c) * 2.0;
                float round = 1.0 - smoothstep(1.0 - _SoftEdge, 1.0, d);

                if (round <= 0.001) discard;

                // ── шум ──
                // Привязываем к МИРОВЫМ координатам, а не только к UV: иначе у
                // всех частиц залпа рисунок клочьев одинаковый, и вместо облака
                // видно строй одинаковых штампов.
                float t = _Time.y * _NoiseSpeed;
                float2 np = i.uv * _NoiseScale + i.posWS.xz * 0.35 + float2(0, -t);

                float n = noise2(np);
                n = lerp(n, n * 0.6 + noise2(np * 2.7 + 11.3) * 0.4, _Detail);

                // ── РАСТВОРЕНИЕ ──
                // Альфа частицы — это ПОРОГ, а не прозрачность. Пока частица
                // «свежая» (alpha высокая), порог низкий и проходит почти весь
                // шум — клуб плотный. К концу жизни порог растёт, шум проходит
                // клочьями, и облако распадается вместо того, чтобы тускнеть.
                float life = saturate(i.color.a);
                float threshold = 1.0 - life * _Density;

                float alpha = smoothstep(threshold, threshold + _EdgeWidth, n);
                alpha *= round;

                if (alpha <= 0.003) discard;

                // ── кромка ──
                // Там, где шум едва перевалил порог, — подсветка. Без неё клочья
                // выглядят вырезанными ножницами.
                float edge = 1.0 - saturate((n - threshold) / _EdgeWidth);
                float3 col = lerp(_Color.rgb, _EdgeColor.rgb * _EdgeGlow, edge * 0.75);

                // Цвет из системы частиц идёт множителем — так Color over
                // Lifetime продолжает работать привычным образом.
                col *= i.color.rgb;

                return half4(col, alpha * _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
