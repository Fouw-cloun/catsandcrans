// ============================================================================
//  CatCooks / Powder Heap   (v17.2 — дешевле, без сетки, с комками; v18 — и на горке/мастерке)
//
//  ПОРОШОК в таре (тёртое какао, мука, сахар) — как какао-порошок в ложке:
//    • КОМКИ: «ячеистый» шум (Воронова клетка) сверху вниз в мировых XZ — каждая клетка
//      круглый комочек: верх светлее, щели между комками темнее; два размера (крупные/мелкие);
//    • свет считается по нормали КОМКОВ (а не по сетке меша) — поэтому нет швов и клеток;
//    • мелкое зерно-пыль, матовый, мультяшная мягкая ступенька света;
//  Цвет — из цвета вершин (цвет продукта). В Linear-проекте переводится из sRGB.
//  Стоимость: 2 прохода по 9 клеток на пиксель, без текстур.
//  v18: если у меша есть нормали (горка на полу, на мастерке) — свет по склону горки + комки;
//       у поверхности тары без нормалей — как раньше, «вверх тары».
// ============================================================================
Shader "CatCooks/Powder Heap"
{
    Properties
    {
        _ClumpScale ("Крупные комки: на метр", Float) = 26
        _SmallScale ("Мелкие комочки: на метр", Float) = 75
        _ClumpAmt ("Комки: сила", Range(0, 1)) = 0.7
        _Crevice ("Тёмные щели", Range(0, 1)) = 0.55
        _GrainScale ("Зерно: на метр", Float) = 380
        _GrainAmt ("Зерно: сила", Range(0, 0.5)) = 0.12
        _ShadowTint ("Цвет тени", Color) = (0.7, 0.62, 0.62, 1)
        _Ambient ("Окружение", Range(0, 2)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        Cull Back

        Pass
        {
            Name "PowderHeap"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ClumpScale, _SmallScale, _GrainScale;
                half _ClumpAmt, _Crevice, _GrainAmt, _Ambient;
                half4 _ShadowTint;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half4 color : COLOR; float3 upWS : TEXCOORD1; float3 nWS : TEXCOORD2; };

            float2 hash22(float2 p)
            {
                float3 a = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                a += dot(a, a.yzx + 33.33);
                return frac((a.xx + a.yz) * a.zy);
            }

            float vn2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash22(i).x, b = hash22(i + float2(1, 0)).x, c = hash22(i + float2(0, 1)).x, d = hash22(i + float2(1, 1)).x;
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // v17.3: НЕРОВНЫЕ куски: у каждой клетки свой размер и своя высота, форма «гнётся» шумом,
            // комок — не купол, а приплюснутая глыба с крутыми краями (рисунок высот).
            // h — высота (0..1), g — наклон «от центра» (для нормали), id — случайное у клетки
            void Cells(float2 p, out float h, out float2 g, out float id)
            {
                float2 w = float2(vn2(p * 0.7 + 3.1), vn2(p * 0.7 + 8.7)) - 0.5;
                p += w * 0.9;                                    // форма неровная
                float2 ip = floor(p), fp = frac(p);
                h = 0; g = 0; id = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 r = hash22(ip + o);
                    float2 v = o + 0.1 + r * 0.8 - fp;           // от точки к центру клетки
                    float rad = 0.32 + 0.5 * r.y;                // разный размер
                    float d = length(v * float2(1.0, 0.7 + 0.6 * r.x));   // вытянутые
                    float k = saturate(1.0 - d / rad);
                    float top = 0.35 + 0.65 * frac(r.x * 7.13 + r.y * 3.7);    // разная высота
                    float hk = smoothstep(0.0, 0.55, k) * top;   // глыба: плато + крутой край
                    if (hk > h)
                    {
                        h = hk; id = r.x;
                        float edge = (k > 0.0 && k < 0.55) ? 2.4 * top / max(0.05, rad) : 0.15;
                        g = (d > 1e-4) ? (-v / max(1e-4, length(v))) * edge : 0;
                    }
                }
            }

            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.upWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                o.nWS = TransformObjectToWorldDir(v.normalOS, false);         // не нормализуем: без нормалей длина 0
                o.color = v.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                // v18.1: проекция по склону — на крутых боках горки комки не вытягиваются в полосы
                float3 up = dot(i.nWS, i.nWS) > 0.25 ? normalize(i.nWS) : i.upWS;
                float3 an = abs(up);
                int ax = an.y > 0.6 ? 1 : (an.x > an.z ? 0 : 2);
                float2 xz = ax == 1 ? i.positionWS.xz : (ax == 0 ? i.positionWS.zy : i.positionWS.xy);
                float h1, h2, id1, id2; float2 g1, g2;
                Cells(xz * _ClumpScale, h1, g1, id1);
                Cells(xz * _SmallScale + 17.3, h2, g2, id2);
                float h = max(h1, h2 * 0.55);                    // мелкие — между крупными
                float2 g = (h1 >= h2 * 0.55 ? g1 : g2 * 0.6) * _ClumpAmt;

                // нормаль: склон горки + наклоны комков в плоскости проекции (не зависит от сетки меша)
                float3 tg = ax == 1 ? float3(g.x, 0, g.y) : (ax == 0 ? float3(0, g.y, g.x) : float3(g.x, g.y, 0));
                float3 n = normalize(up + tg * 0.45);
                // цвет: верх комков чуть светлее, щели темнее, у каждого комка свой оттенок
                alb *= lerp(1.0 - _Crevice * 0.75, 1.12, h);      // низины тёмные, верхушки светлые
                alb *= 0.92 + id1 * 0.16;
                float gr = hash21(floor(xz * _GrainScale));
                alb *= 1.0 + (gr - 0.5) * 2.0 * _GrainAmt;

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, L.direction));
                half lit = smoothstep(0.2, 0.6, ndl) * L.shadowAttenuation;
                half3 shade = lerp(_ShadowTint.rgb * 0.55, 1.0, lit);
                half3 col = alb * (L.color * shade + SampleSH(n) * _Ambient * 0.5);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
